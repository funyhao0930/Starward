using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Core.Launcher;
using Starward.Core.Launcher.Gryphline;
using Starward.Features.GameInstall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Gryphline;

/// <summary>
/// 终末地的安装包信息。
/// <para/>
/// 要不要更新看本机 game_files 的 MD5 是否等于线上的 game_files_md5（本机版本号加密了，读不出来），
/// 与 <see cref="GryphlineDiscoveryProvider"/> 的依据相同。安装大小取整包中央目录里各文件解压后的总和：
/// Starward 是按文件下载，不需要接口 total_size 那种「分卷加解压」的双倍空间。
/// <para/>
/// 预下载要带本机版本号去问，本机版本见 <see cref="GetStateAsync"/>；下载与打补丁在 RPC 进程的 GryphlineGameInstaller。
/// </summary>
internal class GryphlinePackageInfoProvider : IGamePackageInfoProvider, IGamePackageListProvider
{

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);


    private readonly ILogger<GryphlinePackageInfoProvider> _logger;

    private readonly GryphlineLauncherClient _launcherClient;

    private readonly SemaphoreSlim _semaphore = new(1);

    private GryphlineLatestGame? _latest;

    private DateTimeOffset _latestTime;

    private readonly Dictionary<string, (GryphlineLatestGame? Latest, DateTimeOffset Time)> _withLocal = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 中央目录只随版本变，按版本缓存
    /// </summary>
    private (string Version, long Size)? _installSize;


    public GryphlinePackageInfoProvider(ILogger<GryphlinePackageInfoProvider> logger, GryphlineLauncherClient launcherClient)
    {
        _logger = logger;
        _launcherClient = launcherClient;
    }


    public string ProviderId => GryphlineGameMapping.ProviderId;


    public bool Supports(GameKey key) => key == GryphlineGameMapping.EndfieldDefault;


    /// <summary>
    /// GRYPHLINK 的默认安装目录名，游戏在其下的 games\EndField Game
    /// </summary>
    public string GetDefaultFolderName(GameKey key) => "GRYPHLINK";



    public async Task<GamePackageSize?> GetInstallSizeAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        GryphlineLatestGame? latest = await GetLatestGameAsync(cancellationToken);
        if (latest?.Package is not GryphlineGamePackage package || latest.Version is not string version)
        {
            return null;
        }
        if (_installSize is (string cachedVersion, long cachedSize) && cachedVersion == version)
        {
            return new GamePackageSize(version, cachedSize, cachedSize);
        }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            IReadOnlyList<ZipEntryInfo> entries = await _launcherClient.GetPackageEntriesAsync(package, timeout.Token);
            long size = GryphlineDownloadPlanner.GetFiles(entries).Sum(x => x.Size);
            _installSize = (version, size);
            return new GamePackageSize(version, size, size);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Get Endfield package entries");
            return null;
        }
    }



    /// <summary>
    /// 本机版本记在加密的 config.ini 里，不解密它：game_files 与线上一致时本机就是线上版本，
    /// 落后时只有预下载过的才知道（标记里记着）。预下载只在本机是最新版时才问得到。
    /// </summary>
    public async Task<GamePackageState?> GetStateAsync(GameKey key, string installPath, CancellationToken cancellationToken = default)
    {
        GryphlineLatestGame? latest = await GetLatestGameAsync(cancellationToken);
        string? remoteMd5 = latest?.Package?.GameFilesMd5;
        if (latest is null || string.IsNullOrWhiteSpace(remoteMd5))
        {
            return null;
        }
        string? localMd5 = await GetLocalGameFilesMd5Async(installPath, cancellationToken);
        // game_files 是最后才写的，缺了说明上次没装完，同样当作要更新（按文件比对会把缺的补齐）
        bool updateAvailable = !string.Equals(localMd5, remoteMd5, StringComparison.OrdinalIgnoreCase);
        string? localVersion = !updateAvailable ? latest.Version
                             : latest.Version is string target ? GryphlinePatchPlanner.FindMarker(installPath, target)?.LocalVersion
                             : null;

        GryphlinePatch? prePatch = !updateAvailable && localVersion is not null ? (await GetLatestGameAsync(localVersion, cancellationToken))?.PrePatch : null;
        string? predownloadVersion = prePatch?.Patches is { Count: > 0 } ? prePatch.Version : null;
        return new GamePackageState
        {
            LocalVersion = localVersion,
            LatestVersion = latest.Version,
            UpdateAvailable = updateAvailable,
            PredownloadVersion = predownloadVersion,
            PredownloadBytes = predownloadVersion is null ? 0 : prePatch!.Size,
            PredownloadFinished = predownloadVersion is not null && GryphlinePatchPlanner.IsPredownloadFinished(installPath, localVersion!, predownloadVersion),
        };
    }



    /// <summary>
    /// 整包的分卷，与官方启动器下载的是同一批文件。
    /// 本机是最新版、而且有预下载时，另列预下载的补丁分卷（加密的，正式上线后才能解开）。
    /// 落后时的差分包要先知道本机版本，那记在加密的 config.ini 里，这里不列。
    /// </summary>
    public async Task<GamePackageList?> GetPackageListAsync(GameKey key, string? installPath, CancellationToken cancellationToken = default)
    {
        GryphlineLatestGame? latest = await GetLatestGameAsync(cancellationToken);
        if (latest?.Version is not string version || latest.Package?.Packs is not { Count: > 0 } packs)
        {
            return null;
        }
        var list = new GamePackageList
        {
            LatestVersion = version,
            Latest = [new GamePackageGroup(ToEntries(packs))],
        };
        if (installPath is not null
            && string.Equals(await GetLocalGameFilesMd5Async(installPath, cancellationToken), latest.Package.GameFilesMd5, StringComparison.OrdinalIgnoreCase)
            && (await GetLatestGameAsync(version, cancellationToken))?.PrePatch is { Version: string target, Patches: { Count: > 0 } parts })
        {
            list = list with
            {
                PredownloadVersion = target,
                Predownload = [new GamePackageGroup(ToEntries(parts), version)],
            };
        }
        return list;
    }


    /// <summary>
    /// 分卷是同一个 zip 切开的，少一卷就组不回去：没有地址的那卷也照位置列出来（不能复制），不悄悄略过
    /// </summary>
    private static List<GamePackageEntry> ToEntries(List<GryphlinePackageFile> parts)
    {
        return parts.Select((x, i) => string.IsNullOrWhiteSpace(x.Url)
                                    ? new GamePackageEntry($"#{i + 1}", x.Size, x.Md5)
                                    : new GamePackageEntry(GetFileName(x.Url), x.Size, x.Md5, x.Url))
                    .ToList();
    }


    private static string GetFileName(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? Path.GetFileName(uri.AbsolutePath) : Path.GetFileName(url);
    }



    private async Task<GryphlineLatestGame?> GetLatestGameAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_latest is not null && DateTimeOffset.Now - _latestTime < CacheDuration)
            {
                return _latest;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            _latest = await _launcherClient.GetLatestGameAsync(GryphlineLauncherClient.ENDFIELD_APP_CODE, cancellationToken: timeout.Token);
            _latestTime = DateTimeOffset.Now;
            return _latest;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Get Endfield latest game");
            return _latest;
        }
        finally
        {
            _semaphore.Release();
        }
    }



    /// <summary>
    /// 带本机版本号问，才有补丁与预下载。按版本号缓存，问不到时返回上次的结果或 null
    /// </summary>
    private async Task<GryphlineLatestGame?> GetLatestGameAsync(string localVersion, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_withLocal.TryGetValue(localVersion, out var cached) && DateTimeOffset.Now - cached.Time < CacheDuration)
            {
                return cached.Latest;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            GryphlineLatestGame? latest = await _launcherClient.GetLatestGameAsync(GryphlineLauncherClient.ENDFIELD_APP_CODE, localVersion, timeout.Token);
            _withLocal[localVersion] = (latest, DateTimeOffset.Now);
            return latest;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Get Endfield latest game from {version}", localVersion);
            return _withLocal.TryGetValue(localVersion, out var cached) ? cached.Latest : null;
        }
        finally
        {
            _semaphore.Release();
        }
    }



    private async Task<string?> GetLocalGameFilesMd5Async(string installPath, CancellationToken cancellationToken)
    {
        try
        {
            string path = Path.Combine(installPath, GryphlineGameMapping.GameFolderName, GryphlineVersionMapper.GameFilesName);
            if (!File.Exists(path))
            {
                return null;
            }
            await using FileStream fs = File.OpenRead(path);
            return Convert.ToHexStringLower(await MD5.HashDataAsync(fs, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Read Endfield game_files");
            return null;
        }
    }

}
