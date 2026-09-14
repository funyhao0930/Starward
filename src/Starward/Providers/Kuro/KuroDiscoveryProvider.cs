using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Starward.Core.Games;
using Starward.Core.Games.Kuro;
using Starward.Core.Launcher.Kuro;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Kuro;

/// <summary>
/// 搜索已安装的鸣潮。
/// <para/>
/// 卸载项中的 KRInstall 记录可能是旧安装留下的过期值，
/// 因此以官方启动器写入的 HKCU 路径为准。
/// </summary>
internal class KuroDiscoveryProvider : IGameDiscoveryProvider
{

    /// <summary>
    /// 官方启动器记录安装路径的位置，末段是渠道的 appId
    /// </summary>
    private const string LauncherRegistryPathFormat = @"HKEY_CURRENT_USER\SOFTWARE\KuroGame\KRLauncher\Aki_G153_default_{0}";

    private const string InstallPathValueName = "SingleLauncherInstallPath";


    private readonly ILogger<KuroDiscoveryProvider> _logger;

    private readonly IGameCatalogProvider _catalog;

    private readonly KuroLauncherClient _launcherClient;


    /// <summary>
    /// 官方公布的游戏版本号，一个工作阶段内只查一次
    /// </summary>
    private Version? _latestVersion;

    /// <summary>
    /// 上次查询失败的时间。失败不缓存结果，但也不能每次切页都重试。
    /// </summary>
    private DateTimeOffset _lastFailedTime = DateTimeOffset.MinValue;

    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 慢到这个地步就别拖着启动页了
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);


    /// <remarks>
    /// 不能注入 <see cref="IGameProviderRegistry"/>，那会与注册表形成循环依赖。
    /// </remarks>
    public KuroDiscoveryProvider(ILogger<KuroDiscoveryProvider> logger, KuroLauncherClient launcherClient)
    {
        _logger = logger;
        _launcherClient = launcherClient;
        _catalog = new SimpleGameCatalogProvider(KuroGameMapping.ProviderId, KuroGameMapping.GetDescriptors);
    }


    public string ProviderId => KuroGameMapping.ProviderId;


    public ValueTask<GameInstallation?> GetInstallationAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(GetInstallation(key));
    }


    public ValueTask<IReadOnlyList<GameInstallation>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var installations = new List<GameInstallation>();
        foreach (GameKey key in KuroGameMapping.SupportedGameKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetInstallation(key) is GameInstallation installation)
            {
                installations.Add(installation);
            }
        }
        return ValueTask.FromResult<IReadOnlyList<GameInstallation>>(installations.AsReadOnly());
    }



    /// <summary>
    /// 鸣潮把本地版本号记录在游戏目录的 launcherDownloadConfig.json 中
    /// </summary>
    public async ValueTask<Version?> GetLocalVersionAsync(GameKey key, string installPath, CancellationToken cancellationToken = default)
    {
        try
        {
            string path = Path.Combine(installPath, KuroGameMapping.GameFolderName, KuroGameMapping.VersionFileName);
            if (!File.Exists(path))
            {
                return null;
            }
            await using FileStream fs = File.OpenRead(path);
            using JsonDocument doc = await JsonDocument.ParseAsync(fs, cancellationToken: cancellationToken);
            if (doc.RootElement.TryGetProperty("version", out JsonElement element)
                && Version.TryParse(element.GetString(), out Version? version))
            {
                return version;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Read Wuthering Waves local version");
        }
        return null;
    }


    /// <summary>
    /// 鸣潮公布的版本号说的是游戏本体，与 <see cref="GetLocalVersionAsync"/>
    /// 读出来的 launcherDownloadConfig.json 是同一个口径，可以直接比较。
    /// </summary>
    public GameVersionSource LatestVersionSource => GameVersionSource.Game;


    /// <summary>
    /// 官方公布的游戏版本号，取自官方启动器的游戏配置。
    /// <para/>
    /// Starward 没有实现鸣潮的下载器，这个版本号只用来提醒玩家该回官方启动器更新，
    /// 不会把启动按钮接到下载流程上。
    /// </summary>
    public async ValueTask<Version?> GetLatestVersionAsync(GameKey key, string installPath, CancellationToken cancellationToken = default)
    {
        if (key != KuroGameMapping.WutheringWavesGlobal)
        {
            return null;
        }
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
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            KuroLauncherGameIndex? index = await _launcherClient.GetGameIndexAsync(timeout.Token);
            if (Version.TryParse(index?.Default?.Version, out Version? version))
            {
                _latestVersion = version;
                return version;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Get Wuthering Waves latest version");
        }
        _lastFailedTime = DateTimeOffset.Now;
        return null;
    }


    private GameInstallation? GetInstallation(GameKey key)
    {
        if (_catalog.GetGame(key) is not GameDescriptor descriptor)
        {
            return null;
        }

        // 用户已经指定过的目录优先
        if (ProviderInstallPathStore.GetInstallPath(descriptor) is string stored)
        {
            return new GameInstallation
            {
                Key = key,
                InstallPath = stored,
                IsOnRemovableStorage = ProviderInstallPathStore.GetRemovable(descriptor),
                StorageRemoved = ProviderInstallPathStore.GetRemovable(descriptor) && !Directory.Exists(stored),
            };
        }

        string? path = GetInstallPathFromRegistry(key);
        if (Directory.Exists(path))
        {
            ProviderInstallPathStore.SetInstallPath(descriptor, path);
            return new GameInstallation
            {
                Key = key,
                InstallPath = path,
            };
        }
        return null;
    }


    private string? GetInstallPathFromRegistry(GameKey key)
    {
        try
        {
            // 目前只适配国际服
            if (key != KuroGameMapping.WutheringWavesGlobal)
            {
                return null;
            }
            string registryPath = string.Format(LauncherRegistryPathFormat, KuroGameMapping.GlobalAppId);
            return Registry.GetValue(registryPath, InstallPathValueName, null) as string;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Read Wuthering Waves install path from registry");
            return null;
        }
    }

}
