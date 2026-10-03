using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using Starward.Core.Games.Hotta;
using Starward.Core.Launcher.Hotta;
using Starward.Features.GameInstall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Hotta;

/// <summary>
/// 异环游戏本体的线上资源，取自 PatcherSDK 公开的版本配置，见 <see cref="HottaPatcherConfig"/>。
/// <para/>
/// 只有版本与大小：再往下的文件清单是加密的，没有可以复制的下载地址。
/// 资源由官方外壳下载，Starward 不安装异环，这里只是让游戏设置里的那一页说得出线上是哪一版。
/// 资源地址与分支都读本机的配置，没装就查不到。
/// </summary>
internal class HottaPackageListProvider : IGamePackageListProvider
{

    /// <summary>
    /// 版本配置只有几 KB，一个地址慢到这个地步就换下一个。每个地址各自计时，主站卡住时备援才轮得到
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 与鸣潮、终末地一样缓存几分钟：每次打开游戏设置都会问
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 主站与备援都取不到时，这段时间内不再重试，免得每次打开对话框都空等
    /// </summary>
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);


    private readonly ILogger<HottaPackageListProvider> _logger;

    private readonly HottaLauncherClient _client;

    private readonly IMemoryCache _memoryCache;


    public HottaPackageListProvider(ILogger<HottaPackageListProvider> logger, HottaLauncherClient client, IMemoryCache memoryCache)
    {
        _logger = logger;
        _client = client;
        _memoryCache = memoryCache;
    }


    public string ProviderId => HottaGameMapping.ProviderId;


    public bool Supports(GameKey key) => key == HottaGameMapping.NevernessToEvernessTaiwan;


    public async Task<GamePackageList?> GetPackageListAsync(GameKey key, string? installPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }
        try
        {
            if (await GetSettingsAsync(installPath, cancellationToken) is not HottaPatcherSettings settings)
            {
                return null;
            }
            IReadOnlyList<string> urls = HottaPatcherConfig.GetVersionConfigUrls(settings);
            // 缓存的是版本配置本身，名称每次照当前的界面语言生成
            string cacheKey = $"{nameof(HottaPackageListProvider)}_{string.Join('|', urls)}";
            if (!_memoryCache.TryGetValue(cacheKey, out HottaResourceVersion? version))
            {
                version = await GetResourceVersionAsync(urls, cancellationToken);
                _memoryCache.Set(cacheKey, version, version is null ? RetryInterval : CacheDuration);
            }
            if (version is null)
            {
                return null;
            }
            // BaseVerson 里没列本体时，用顶层的 ResSize
            List<GamePackageEntry> baseFiles = version.Tags.Where(x => x.IsBase).Select(x => new GamePackageEntry(GetTagName(x), x.Size)).ToList();
            if (baseFiles.Count == 0 && version.Size > 0)
            {
                baseFiles.Add(new GamePackageEntry(Lang.GameResourcePage_BaseResources, version.Size));
            }
            // 附加资源由游戏自己按需下载，不是完整安装的一部分，另成一组
            List<GamePackageEntry> extraFiles = version.Tags.Where(x => !x.IsBase).Select(x => new GamePackageEntry(GetTagName(x), x.Size)).ToList();
            var groups = new List<GamePackageGroup>();
            if (baseFiles.Count > 0)
            {
                groups.Add(new GamePackageGroup(baseFiles));
            }
            if (extraFiles.Count > 0)
            {
                groups.Add(new GamePackageGroup(extraFiles, Name: Lang.GameResourcePage_AdditionalResources));
            }
            return new GamePackageList
            {
                LatestVersion = version.Version,
                Latest = groups,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Get Neverness to Everness resource version");
            return null;
        }
    }


    /// <summary>
    /// 主站在前，逐个地址尝试，每个地址各自计时。都取不到返回 null，每次失败的原因都记下来
    /// </summary>
    private async Task<HottaResourceVersion?> GetResourceVersionAsync(IReadOnlyList<string> urls, CancellationToken cancellationToken)
    {
        foreach (string url in urls)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(RequestTimeout);
                if (await _client.GetResourceVersionAsync(url, timeout.Token) is HottaResourceVersion version)
                {
                    return version;
                }
                _logger.LogWarning("Cannot parse Neverness to Everness resource version from {url}", url);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // 超时、拒绝连接、地址格式不对都换下一个
                _logger.LogWarning(ex, "Get Neverness to Everness resource version from {url}", url);
            }
        }
        return null;
    }


    /// <summary>
    /// 外壳 Config.ini 指出 PatcherSDK 的配置目录，那里的 PatcherConfig.json 给出资源地址与分支
    /// </summary>
    private async Task<HottaPatcherSettings?> GetSettingsAsync(string installPath, CancellationToken cancellationToken)
    {
        string configIni = Path.Combine(installPath, HottaGameMapping.ConfigFileRelativePath);
        if (!File.Exists(configIni))
        {
            _logger.LogWarning("Neverness to Everness config file not found ({path})", configIni);
            return null;
        }
        if (HottaPatcherConfig.ParseConfigPath(await File.ReadAllTextAsync(configIni, cancellationToken)) is not string configPath)
        {
            _logger.LogWarning("Cannot find [Patcher] configPath in {path}", configIni);
            return null;
        }
        string folder = configPath.Trim('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        string settingsPath = Path.Combine(installPath, HottaGameMapping.GameFolderName, folder, HottaPatcherConfig.SettingsFileName);
        if (!File.Exists(settingsPath))
        {
            _logger.LogWarning("Neverness to Everness patcher config not found ({path})", settingsPath);
            return null;
        }
        if (HottaPatcherConfig.ParseSettings(await File.ReadAllTextAsync(settingsPath, cancellationToken)) is not HottaPatcherSettings settings)
        {
            _logger.LogWarning("Cannot parse Neverness to Everness patcher config ({path})", settingsPath);
            return null;
        }
        return settings;
    }


    /// <summary>
    /// 本体叫「基础资源」，版本就是标题上的那个。其余标签（pakchunk101 这类）由游戏自己按需下载，
    /// 官方没有给名称，照原样显示，后面带上它自己的版本
    /// </summary>
    private static string GetTagName(HottaResourceTag tag)
    {
        if (tag.IsBase)
        {
            return Lang.GameResourcePage_BaseResources;
        }
        return string.IsNullOrWhiteSpace(tag.Version) ? tag.Tag : $"{tag.Tag}  {tag.Version}";
    }

}
