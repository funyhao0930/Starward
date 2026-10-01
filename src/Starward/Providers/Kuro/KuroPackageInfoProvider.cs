using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using Starward.Core.Games.Kuro;
using Starward.Core.Launcher.Kuro;
using Starward.Features.GameInstall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Kuro;

/// <summary>
/// 鸣潮的安装包信息，取自官方启动器的游戏配置：旧启动器那份（index.json，只有 HD）
/// 与新启动器的分级配置（official/index.json）。
/// <para/>
/// 走哪一份与 RPC 里的安装器用同一套规则（<see cref="KuroResourcePackPlanner.ChooseSource"/>），
/// 否则这里说有更新、安装器却拿另一份的版本去比，按钮就会接到一个什么都不做的任务上。
/// <para/>
/// 配置在 CDN 上，几分钟内重复打开启动页不必每次都问，
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

    private KuroOfficialGameIndex? _tieredIndex;

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
        (KuroLauncherGameIndex? index, KuroOfficialGameIndex? tiered) = await GetIndexesAsync(cancellationToken);
        if (index?.Default?.Config is KuroLauncherGameConfig config)
        {
            string version = config.Version ?? index.Default.Version ?? "";
            // unCompressSize 与 size 相同：鸣潮是逐个文件下载，没有压缩包
            return new GamePackageSize(version, config.Size, Math.Max(config.Size, config.UnCompressSize));
        }
        // 旧版配置读不到时，默认安装的 HD 由分级配置提供
        if (tiered is not null && KuroResourcePackPlanner.GetTierSizes(tiered).FirstOrDefault(x => x.Tier == KuroResourceTier.Default) is KuroResourceTierSize hd)
        {
            return new GamePackageSize(KuroResourcePackPlanner.GetVersion(tiered.ResourcePacks) ?? "", hd.TotalBytes, hd.TotalBytes);
        }
        return null;
    }



    public async Task<IReadOnlyList<GameResourceTierPackage>?> GetResourceTiersAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        (_, KuroOfficialGameIndex? tiered) = await GetIndexesAsync(cancellationToken);
        if (tiered is null)
        {
            return null;
        }
        List<GameResourceTierPackage> tiers = KuroResourcePackPlanner.GetTierSizes(tiered)
                                                                      .Select(x => new GameResourceTierPackage(x.Tier, x.TotalBytes, x.TierBytes))
                                                                      .ToList();
        return tiers.Count > 0 ? tiers.AsReadOnly() : null;
    }



    public async Task<GamePackageState?> GetStateAsync(GameKey key, string installPath, CancellationToken cancellationToken = default)
    {
        (KuroLauncherGameIndex? index, KuroOfficialGameIndex? tiered) = await GetIndexesAsync(cancellationToken);
        if (index?.Default is null && tiered is null)
        {
            return null;
        }
        string gameDir = Path.Combine(installPath, KuroGameMapping.GameFolderName);
        string? local = ReadLocalVersion(gameDir);
        // 与安装器一样：照本机装了的分级；3.7.0 以前的安装没有分级目录，更新上来就是 HD
        IReadOnlyList<string> installed = KuroResourceTier.GetInstalledTiers(gameDir);
        IReadOnlyList<string> tiers = installed.Count > 0 ? installed : [KuroResourceTier.Default];

        string? legacyVersion = index?.Default?.Config?.Version ?? index?.Default?.Version;
        string? tieredVersion = tiered is not null && KuroResourcePackPlanner.GetPacks(tiered.ResourcePacks, tiered.Bundles, tiers) is not null
                              ? KuroResourcePackPlanner.GetVersion(tiered.ResourcePacks)
                              : null;
        string? latest = KuroResourcePackPlanner.ChooseSource(tiers, legacyVersion, tieredVersion) switch
        {
            KuroDownloadSource.Legacy => legacyVersion,
            KuroDownloadSource.Tiered => tieredVersion,
            _ => legacyVersion ?? tieredVersion,
        };
        bool updateAvailable = local is not null && latest is not null && KuroResourcePackPlanner.CompareVersion(local, latest) < 0;

        string? predownloadVersion = null;
        long predownloadBytes = 0;
        bool predownloadFinished = false;
        if (!updateAvailable && local is not null && GetPredownload(index, tiered, tiers, installed, local) is (string target, long bytes, KuroDownloadSource source))
        {
            predownloadVersion = target;
            predownloadBytes = bytes;
            predownloadFinished = KuroDownloadPlanner.IsPredownloadFinished(installPath, local, target, source, tiers);
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



    /// <summary>
    /// 能预下载时返回目标版本、要下载的字节数与走哪一份配置。选哪一份配置的规则与安装器相同。
    /// <para/>
    /// 有补丁的资源包算补丁大小。没有补丁的，安装器会把本机没有、或大小不同的文件先下载到暂存目录；
    /// 这里只认得出整档都没装的那种（例如 3.6.x 更新到 3.7.0 时新出现的 HD 包），按整包算，
    /// 其余的要逐个文件比对才知道，不计入。
    /// </summary>
    private static (string Version, long Bytes, KuroDownloadSource Source)? GetPredownload(KuroLauncherGameIndex? index, KuroOfficialGameIndex? tiered,
                                                                                         IReadOnlyList<string> tiers, IReadOnlyList<string> installed, string local)
    {
        KuroLauncherGameConfig? legacyConfig = index?.PredownloadSwitch == 1 && index.Predownload?.Config is { IndexFile.Length: > 0, BaseUrl.Length: > 0 } c ? c : null;
        string? legacyVersion = legacyConfig is null ? null : legacyConfig.Version ?? index!.Predownload!.Version;

        KuroOfficialPredownload? predownload = tiered?.Config?.PredownloadSwitch == 1 ? KuroResourcePackPlanner.GetPredownload(tiered) : null;
        IReadOnlyList<KuroResourcePack>? packs = predownload is null ? null : KuroResourcePackPlanner.GetPacks(predownload.ResourcePacks, predownload.Bundles, tiers);
        string? tieredVersion = packs is null ? null : KuroResourcePackPlanner.GetVersion(predownload!.ResourcePacks);

        KuroDownloadSource? source = KuroResourcePackPlanner.ChooseSource(tiers, legacyVersion, tieredVersion);
        (string? version, long bytes) = source switch
        {
            KuroDownloadSource.Legacy => (legacyVersion, KuroDownloadPlanner.FindPatch(legacyConfig!, local)?.Size ?? 0),
            KuroDownloadSource.Tiered => (tieredVersion, packs!.Sum(x => KuroDownloadPlanner.FindPatch(x.Config, local)?.Size
                                                                        ?? (KuroResourceTier.Normalize(x.Name) is string tier && !installed.Contains(tier) ? x.Config.Size : 0))),
            _ => (null, 0),
        };
        if (source is KuroDownloadSource s && version is not null && bytes > 0 && KuroResourcePackPlanner.CompareVersion(version, local) > 0)
        {
            return (version, bytes, s);
        }
        return null;
    }



    /// <summary>
    /// 两份配置，一起问、一起缓存。任何一份读不到都不影响另一份。
    /// </summary>
    private async Task<(KuroLauncherGameIndex? Legacy, KuroOfficialGameIndex? Tiered)> GetIndexesAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if ((_index is not null || _tieredIndex is not null) && DateTimeOffset.Now - _indexTime < CacheDuration)
            {
                return (_index, _tieredIndex);
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            Task<KuroLauncherGameIndex?> legacy = GetAsync(_launcherClient.GetGameIndexAsync, _index, "game index", timeout.Token, cancellationToken);
            Task<KuroOfficialGameIndex?> tiered = GetAsync(_launcherClient.GetOfficialGameIndexAsync, _tieredIndex, "tiered game index", timeout.Token, cancellationToken);
            await Task.WhenAll(legacy, tiered);
            _index = legacy.Result;
            _tieredIndex = tiered.Result;
            _indexTime = DateTimeOffset.Now;
            return (_index, _tieredIndex);
        }
        finally
        {
            _semaphore.Release();
        }
    }


    /// <summary>
    /// 读一份配置，失败时沿用上次的
    /// </summary>
    private async Task<T?> GetAsync<T>(Func<CancellationToken, Task<T?>> get, T? previous, string name, CancellationToken timeoutToken, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await get(timeoutToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Get Wuthering Waves {name}", name);
            return previous;
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

}
