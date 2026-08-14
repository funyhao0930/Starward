using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Starward.Core;
using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Core.HoYoPlay;
using Starward.Features.GameLauncher;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.HoYo;

/// <summary>
/// 搜索已安装的米哈游游戏。
/// 先读取应用配置中记录的安装目录，找不到时再查询 HoYoPlay 启动器写入的注册表。
/// </summary>
internal class HoYoDiscoveryProvider : IGameDiscoveryProvider
{

    /// <summary>
    /// HoYoPlay 国服启动器的注册表位置，按新到旧排列。
    /// </summary>
    private static readonly string[] HypRegistryPaths_China =
    [
        @"HKEY_CURRENT_USER\Software\miHoYo\HYP\1_1",
        @"HKEY_CURRENT_USER\Software\miHoYo\HYP\1_0",
    ];

    /// <summary>
    /// HoYoPlay 国际服启动器的注册表位置，按新到旧排列。
    /// <para/>
    /// 启动器把这一段版本号从 1_0 升到了 1_1，只查旧位置会找不到任何国际服游戏，
    /// 因此两个都要查。
    /// </summary>
    private static readonly string[] HypRegistryPaths_Global =
    [
        @"HKEY_CURRENT_USER\Software\Cognosphere\HYP\1_1",
        @"HKEY_CURRENT_USER\Software\Cognosphere\HYP\1_0",
    ];

    private const string GameInstallPathValueName = "GameInstallPath";


    private readonly ILogger<HoYoDiscoveryProvider> _logger;

    private readonly IHoYoGameInfoSource _gameInfoSource;


    public HoYoDiscoveryProvider(ILogger<HoYoDiscoveryProvider> logger, IHoYoGameInfoSource gameInfoSource)
    {
        _logger = logger;
        _gameInfoSource = gameInfoSource;
    }


    public string ProviderId => HoYoGameMapping.ProviderId;


    public ValueTask<GameInstallation?> GetInstallationAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(GetInstallation(key, probeRegistry: true));
    }


    public ValueTask<IReadOnlyList<GameInstallation>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var installations = new List<GameInstallation>();
        foreach (GameKey key in GetCandidateGameKeys())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetInstallation(key, probeRegistry: true) is GameInstallation installation)
            {
                installations.Add(installation);
            }
        }
        return ValueTask.FromResult<IReadOnlyList<GameInstallation>>(installations.AsReadOnly());
    }



    /// <summary>
    /// 已适配的游戏，加上 HoYoPlay 接口返回但尚未适配的游戏
    /// </summary>
    private IEnumerable<GameKey> GetCandidateGameKeys()
    {
        var added = new HashSet<GameKey>();
        foreach (GameKey key in HoYoGameMapping.SupportedGameKeys)
        {
            if (added.Add(key))
            {
                yield return key;
            }
        }
        foreach (GameInfo info in _gameInfoSource.GetCachedGameInfos())
        {
            if (!HoYoGameMapping.TryFromGameBiz(info.GameBiz, out GameKey key))
            {
                continue;
            }
            if (info.IsBilibiliServer())
            {
                key = key with { ChannelId = GameChannelIds.Bilibili };
            }
            if (added.Add(key))
            {
                yield return key;
            }
        }
    }


    private GameInstallation? GetInstallation(GameKey key, bool probeRegistry)
    {
        if (!HoYoGameMapping.TryToGameBiz(key, out GameBiz gameBiz))
        {
            return null;
        }

        // 应用配置中记录的安装目录
        string? path = GameLauncherService.GetGameInstallPath(gameBiz);
        if (!string.IsNullOrWhiteSpace(path))
        {
            bool removable = AppConfig.GetGameInstallPathRemovable(gameBiz);
            return new GameInstallation
            {
                Key = key,
                InstallPath = path,
                IsOnRemovableStorage = removable,
                StorageRemoved = removable && !Directory.Exists(path),
            };
        }

        if (!probeRegistry)
        {
            return null;
        }

        // HoYoPlay 启动器写入的注册表
        path = GetInstallPathFromRegistry(gameBiz);
        if (Directory.Exists(path))
        {
            AppConfig.SetGameInstallPath(gameBiz, path);
            return new GameInstallation
            {
                Key = key,
                InstallPath = path,
            };
        }

        return null;
    }


    private string? GetInstallPathFromRegistry(GameBiz gameBiz)
    {
        string[] roots = gameBiz.Server switch
        {
            GameChannelIds.China => HypRegistryPaths_China,
            GameChannelIds.Global => HypRegistryPaths_Global,
            _ => [],
        };
        foreach (string root in roots)
        {
            try
            {
                if (Registry.GetValue($@"{root}\{gameBiz}", GameInstallPathValueName, null) is string path
                    && !string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Read install path from registry {root}: {biz}", root, gameBiz);
            }
        }
        return null;
    }

}
