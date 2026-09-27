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
/// 预下载不支持：pre_patch 还没有实际见过，不猜它的格式。
/// </summary>
internal class GryphlinePackageInfoProvider : IGamePackageInfoProvider
{

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);


    private readonly ILogger<GryphlinePackageInfoProvider> _logger;

    private readonly GryphlineLauncherClient _launcherClient;

    private readonly SemaphoreSlim _semaphore = new(1);

    private GryphlineLatestGame? _latest;

    private DateTimeOffset _latestTime;

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



    public async Task<GamePackageState?> GetStateAsync(GameKey key, string installPath, CancellationToken cancellationToken = default)
    {
        GryphlineLatestGame? latest = await GetLatestGameAsync(cancellationToken);
        string? remoteMd5 = latest?.Package?.GameFilesMd5;
        if (latest is null || string.IsNullOrWhiteSpace(remoteMd5))
        {
            return null;
        }
        string? localMd5 = await GetLocalGameFilesMd5Async(installPath, cancellationToken);
        return new GamePackageState
        {
            LatestVersion = latest.Version,
            // game_files 是最后才写的，缺了说明上次没装完，同样当作要更新（按文件比对会把缺的补齐）
            UpdateAvailable = !string.Equals(localMd5, remoteMd5, StringComparison.OrdinalIgnoreCase),
        };
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
            _latest = await _launcherClient.GetLatestGameAsync(GryphlineLauncherClient.ENDFIELD_APP_CODE, timeout.Token);
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
