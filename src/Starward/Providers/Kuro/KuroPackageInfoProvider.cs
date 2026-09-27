using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using Starward.Core.Games.Kuro;
using Starward.Core.Launcher.Kuro;
using Starward.Features.GameInstall;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Kuro;

/// <summary>
/// 鸣潮的安装包信息，全部取自官方启动器的游戏配置（index.json）。
/// <para/>
/// 这份配置在 CDN 上，几分钟内重复打开启动页不必每次都问，
/// 但也不能像 <see cref="KuroDiscoveryProvider"/> 那样整个工作阶段只问一次：
/// 预下载与更新都是在 Starward 开着的时候上线的。
/// </summary>
internal class KuroPackageInfoProvider : IGamePackageInfoProvider
{

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);


    private readonly ILogger<KuroPackageInfoProvider> _logger;

    private readonly KuroLauncherClient _launcherClient;

    private readonly SemaphoreSlim _semaphore = new(1);

    private KuroLauncherGameIndex? _index;

    private DateTimeOffset _indexTime;


    public KuroPackageInfoProvider(ILogger<KuroPackageInfoProvider> logger, KuroLauncherClient launcherClient)
    {
        _logger = logger;
        _launcherClient = launcherClient;
    }


    public string ProviderId => KuroGameMapping.ProviderId;


    public bool Supports(GameKey key) => key == KuroGameMapping.WutheringWavesGlobal;


    /// <summary>
    /// 官方安装程序的默认目录名，游戏本体再往下一层是 Wuthering Waves Game
    /// </summary>
    public string GetDefaultFolderName(GameKey key) => "Wuthering Waves";



    public async Task<GamePackageSize?> GetInstallSizeAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        KuroLauncherGameIndex? index = await GetIndexAsync(cancellationToken);
        if (index?.Default?.Config is not KuroLauncherGameConfig config)
        {
            return null;
        }
        string version = config.Version ?? index.Default.Version ?? "";
        // unCompressSize 与 size 相同：鸣潮是逐个文件下载，没有压缩包
        return new GamePackageSize(version, config.Size, Math.Max(config.Size, config.UnCompressSize));
    }



    public async Task<GamePackageState?> GetStateAsync(GameKey key, string installPath, CancellationToken cancellationToken = default)
    {
        KuroLauncherGameIndex? index = await GetIndexAsync(cancellationToken);
        if (index?.Default is null)
        {
            return null;
        }
        string? local = ReadLocalVersion(Path.Combine(installPath, KuroGameMapping.GameFolderName));
        string? latest = index.Default.Config?.Version ?? index.Default.Version;
        bool updateAvailable = local is not null && latest is not null && CompareVersion(local, latest) < 0;

        string? predownloadVersion = null;
        long predownloadBytes = 0;
        bool predownloadFinished = false;
        // 预下载只在有补丁时开放：没有补丁就只能等正式更新后按完整清单比对，
        // 那要把整个游戏读一遍，放在预下载阶段做没有意义
        if (!updateAvailable
            && local is not null
            && index.PredownloadSwitch == 1
            && index.Predownload?.Config is KuroLauncherGameConfig predownload
            && (predownload.Version ?? index.Predownload.Version) is string target
            && CompareVersion(target, local) > 0
            && KuroDownloadPlanner.FindPatch(predownload, local) is KuroLauncherPatchConfig patch)
        {
            predownloadVersion = target;
            predownloadBytes = patch.Size;
            predownloadFinished = KuroDownloadPlanner.IsPredownloadFinished(installPath, local, target);
        }

        return new GamePackageState
        {
            LocalVersion = local,
            LatestVersion = latest,
            UpdateAvailable = updateAvailable,
            PredownloadVersion = predownloadVersion,
            PredownloadBytes = predownloadBytes,
            PredownloadFinished = predownloadFinished,
        };
    }



    private async Task<KuroLauncherGameIndex?> GetIndexAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_index is not null && DateTimeOffset.Now - _indexTime < CacheDuration)
            {
                return _index;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            _index = await _launcherClient.GetGameIndexAsync(timeout.Token);
            _indexTime = DateTimeOffset.Now;
            return _index;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Get Wuthering Waves game index");
            return _index;
        }
        finally
        {
            _semaphore.Release();
        }
    }



    /// <summary>
    /// 本机版本，与 <see cref="KuroDiscoveryProvider.GetLocalVersionAsync"/> 读的是同一个文件，
    /// 但这里要原样的字符串，好与线上版本、预下载标记比对
    /// </summary>
    private static string? ReadLocalVersion(string gameDir)
    {
        try
        {
            string path = Path.Combine(gameDir, KuroGameMapping.VersionFileName);
            if (!File.Exists(path))
            {
                return null;
            }
            using FileStream fs = File.OpenRead(path);
            using JsonDocument doc = JsonDocument.Parse(fs);
            if (doc.RootElement.TryGetProperty("version", out JsonElement element) && element.GetString() is string version && !string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        return null;
    }


    private static int CompareVersion(string a, string b)
    {
        if (Version.TryParse(a, out Version? va) && Version.TryParse(b, out Version? vb))
        {
            return va.CompareTo(vb);
        }
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

}
