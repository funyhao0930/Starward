using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Starward.Core.Games;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers;

/// <summary>
/// 通过 Windows 卸载项查找安装目录的通用搜索 Provider。
/// 适用于官方启动器会在卸载项中写入 InstallLocation 的游戏。
/// </summary>
internal abstract class UninstallRegistryDiscoveryProvider : IGameDiscoveryProvider
{

    private static readonly string[] UninstallRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];


    protected ILogger Logger { get; }

    private readonly IGameCatalogProvider _catalog;


    /// <param name="catalog">本供应商自己的目录。
    /// 不能注入 <see cref="IGameProviderRegistry"/>，那会与注册表形成循环依赖。</param>
    protected UninstallRegistryDiscoveryProvider(ILogger logger, IGameCatalogProvider catalog)
    {
        Logger = logger;
        _catalog = catalog;
    }


    public abstract string ProviderId { get; }


    /// <summary>
    /// 本供应商支持的游戏
    /// </summary>
    protected abstract IReadOnlyList<GameKey> SupportedGameKeys { get; }


    /// <summary>
    /// 卸载项的键名，为空时改用 <see cref="GetUninstallDisplayName"/> 匹配
    /// </summary>
    protected virtual string? GetUninstallKeyName(GameKey key) => null;


    /// <summary>
    /// 卸载项的显示名称，用于键名不固定的启动器
    /// </summary>
    protected virtual string? GetUninstallDisplayName(GameKey key) => null;


    /// <summary>
    /// 从卸载项记录的目录推算游戏目录。默认原样返回。
    /// </summary>
    protected virtual string? ResolveGameFolder(GameKey key, string installLocation) => installLocation;



    public ValueTask<GameInstallation?> GetInstallationAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(GetInstallation(key));
    }


    public ValueTask<IReadOnlyList<GameInstallation>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var installations = new List<GameInstallation>();
        foreach (GameKey key in SupportedGameKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetInstallation(key) is GameInstallation installation)
            {
                installations.Add(installation);
            }
        }
        return ValueTask.FromResult<IReadOnlyList<GameInstallation>>(installations.AsReadOnly());
    }



    private GameInstallation? GetInstallation(GameKey key)
    {
        if (_catalog.GetGame(key) is not GameDescriptor descriptor)
        {
            return null;
        }

        if (ProviderInstallPathStore.GetInstallPath(descriptor) is string stored)
        {
            bool removable = ProviderInstallPathStore.GetRemovable(descriptor);
            return new GameInstallation
            {
                Key = key,
                InstallPath = stored,
                IsOnRemovableStorage = removable,
                StorageRemoved = removable && !Directory.Exists(stored),
            };
        }

        string? path = FindInstallLocation(key);
        if (!string.IsNullOrWhiteSpace(path))
        {
            path = ResolveGameFolder(key, path.Trim().Trim('"'));
        }
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


    private string? FindInstallLocation(GameKey key)
    {
        string? keyName = GetUninstallKeyName(key);
        string? displayName = GetUninstallDisplayName(key);
        foreach (string root in UninstallRoots)
        {
            try
            {
                using RegistryKey? uninstall = Registry.LocalMachine.OpenSubKey(root);
                if (uninstall is null)
                {
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(keyName))
                {
                    using RegistryKey? entry = uninstall.OpenSubKey(keyName);
                    if (entry?.GetValue("InstallLocation") is string location && !string.IsNullOrWhiteSpace(location))
                    {
                        return location;
                    }
                    continue;
                }
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }
                // 键名不固定的启动器，按显示名称匹配
                foreach (string subKeyName in uninstall.GetSubKeyNames())
                {
                    using RegistryKey? entry = uninstall.OpenSubKey(subKeyName);
                    if (entry?.GetValue("DisplayName") as string == displayName
                        && entry.GetValue("InstallLocation") is string location
                        && !string.IsNullOrWhiteSpace(location))
                    {
                        return location;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Read install location from uninstall registry: {key}", key);
            }
        }
        return null;
    }

}
