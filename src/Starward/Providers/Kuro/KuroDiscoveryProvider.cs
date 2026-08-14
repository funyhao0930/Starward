using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Starward.Core.Games;
using Starward.Core.Games.Kuro;
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


    /// <remarks>
    /// 不能注入 <see cref="IGameProviderRegistry"/>，那会与注册表形成循环依赖。
    /// </remarks>
    public KuroDiscoveryProvider(ILogger<KuroDiscoveryProvider> logger)
    {
        _logger = logger;
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
