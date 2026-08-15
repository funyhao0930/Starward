using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using Starward.Core.Games.Hotta;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Hotta;

/// <summary>
/// 搜索已安装的异环。官方启动器在卸载项中写入了 InstallLocation，
/// 键名由启动器 Config.ini 的 [Global] reg 指定。
/// </summary>
internal class HottaDiscoveryProvider : UninstallRegistryDiscoveryProvider
{

    private readonly HttpClient _httpClient;


    /// <summary>
    /// 官方公布的版本号，一个工作阶段内只查一次
    /// </summary>
    private Version? _latestVersion;

    /// <summary>
    /// 上次查询失败的时间。失败不缓存结果，但也不能每次切页都重试。
    /// </summary>
    private DateTimeOffset _lastFailedTime = DateTimeOffset.MinValue;

    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 版本文件只有一百多字节，慢到这个地步就别拖着启动页了
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);


    public HottaDiscoveryProvider(ILogger<HottaDiscoveryProvider> logger, HttpClient httpClient)
        : base(logger, new SimpleGameCatalogProvider(HottaGameMapping.ProviderId, HottaGameMapping.GetDescriptors))
    {
        _httpClient = httpClient;
    }


    public override string ProviderId => HottaGameMapping.ProviderId;


    protected override IReadOnlyList<GameKey> SupportedGameKeys => HottaGameMapping.SupportedGameKeys;


    protected override string? GetUninstallKeyName(GameKey key)
    {
        return key == HottaGameMapping.NevernessToEvernessTaiwan ? HottaGameMapping.TaiwanUninstallKey : null;
    }


    /// <summary>
    /// 异环把本地版本号记录在官方启动器的 Config.ini 中：[VERSION] Version=1.0.8.0727
    /// </summary>
    public override async ValueTask<Version?> GetLocalVersionAsync(GameKey key, string installPath, CancellationToken cancellationToken = default)
    {
        try
        {
            return HottaGameMapping.ParseVersion(await ReadConfigFileAsync(installPath, cancellationToken));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Read Neverness to Everness local version");
        }
        return null;
    }


    /// <summary>
    /// 官方公布的版本号，取自游戏 Config.ini 里记着的版本文件地址。
    /// <para/>
    /// 注意它描述的是官方启动器（更新程序）这个包的版本，也就是官方启动器
    /// 自我更新的依据；游戏本体资源走另一条 clientRes 的路，那里没有明文
    /// 记载的清单地址，因此不做。界面上的措辞必须与此一致。
    /// </summary>
    public override async ValueTask<Version?> GetLatestVersionAsync(GameKey key, string installPath, CancellationToken cancellationToken = default)
    {
        if (_latestVersion is not null)
        {
            return _latestVersion;
        }
        if (DateTimeOffset.Now - _lastFailedTime < RetryInterval)
        {
            return null;
        }
        try
        {
            string? config = await ReadConfigFileAsync(installPath, cancellationToken);
            IReadOnlyList<string> urls = HottaGameMapping.ParseVersionInfoUrls(config);
            foreach (string url in urls)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(RequestTimeout);
                    string text = await _httpClient.GetStringAsync(url, timeout.Token);
                    if (HottaGameMapping.ParseVersion(text) is Version version)
                    {
                        _latestVersion = version;
                        return version;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
                {
                    // 主站不通就换备援
                    Logger.LogDebug("Cannot get Neverness to Everness version info, try the next address.");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Get Neverness to Everness latest version");
        }
        _lastFailedTime = DateTimeOffset.Now;
        return null;
    }


    private static async Task<string?> ReadConfigFileAsync(string installPath, CancellationToken cancellationToken)
    {
        string path = Path.Combine(installPath, HottaGameMapping.ConfigFileRelativePath);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }

}
