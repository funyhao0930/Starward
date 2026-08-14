namespace Starward.Core.Games.HoYo;

/// <summary>
/// 米哈游游戏的静态映射表，是本供应商所有游戏专属常量的唯一来源：
/// <see cref="GameBiz"/> 与 <see cref="GameKey"/> 的互转、进程名、能力、图标、截图目录、注册表键。
/// <para/>
/// HoYoPlay 的远程 GameId 与 LauncherId 只保留在本供应商内部
/// （见 <see cref="Starward.Core.HoYoPlay.GameId"/>、<see cref="Starward.Core.HoYoPlay.LauncherId"/>），
/// 通用的 <see cref="GameKey"/> 不包含它们。
/// </summary>
public static class HoYoGameMapping
{

    /// <summary>
    /// 供应商标识
    /// </summary>
    public const string ProviderId = GameProviderIds.HoYo;


    /// <summary>
    /// 崩坏3
    /// </summary>
    public const string Bh3 = GameBiz.bh3;

    /// <summary>
    /// 原神
    /// </summary>
    public const string Hk4e = GameBiz.hk4e;

    /// <summary>
    /// 崩坏：星穹铁道
    /// </summary>
    public const string Hkrpg = GameBiz.hkrpg;

    /// <summary>
    /// 绝区零
    /// </summary>
    public const string Nap = GameBiz.nap;



    /// <summary>
    /// 所有已适配的游戏与渠道，与 <see cref="GameBiz.AllGameBizs"/> 一一对应
    /// </summary>
    public static IReadOnlyList<GameKey> SupportedGameKeys { get; } = GameBiz.AllGameBizs.Select(FromGameBiz).ToList().AsReadOnly();



    #region GameBiz 兼容层


    /// <summary>
    /// <see cref="GameBiz"/> 转换为 <see cref="GameKey"/>。
    /// 只要形如 <c>game_server</c> 即可转换，不要求是已适配的游戏。
    /// </summary>
    public static bool TryFromGameBiz(GameBiz gameBiz, out GameKey key)
    {
        key = default;
        string game = gameBiz.Game;
        string server = gameBiz.Server;
        if (string.IsNullOrEmpty(game) || string.IsNullOrEmpty(server))
        {
            return false;
        }
        key = new GameKey(ProviderId, game, server);
        return true;
    }


    /// <summary>
    /// <see cref="GameBiz"/> 转换为 <see cref="GameKey"/>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">不是形如 <c>game_server</c> 的 GameBiz</exception>
    public static GameKey FromGameBiz(GameBiz gameBiz)
    {
        if (TryFromGameBiz(gameBiz, out GameKey key))
        {
            return key;
        }
        throw new ArgumentOutOfRangeException(nameof(gameBiz), gameBiz.Value, "Cannot convert GameBiz to GameKey.");
    }


    /// <summary>
    /// <see cref="GameKey"/> 转换为 <see cref="GameBiz"/>，不属于本供应商时返回 false
    /// </summary>
    public static bool TryToGameBiz(GameKey key, out GameBiz gameBiz)
    {
        gameBiz = default;
        if (!key.IsProvider(ProviderId) || !key.IsValid)
        {
            return false;
        }
        gameBiz = new GameBiz($"{key.GameId}_{key.ChannelId}");
        return true;
    }


    /// <summary>
    /// <see cref="GameKey"/> 转换为 <see cref="GameBiz"/>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">不属于本供应商</exception>
    public static GameBiz ToGameBiz(GameKey key)
    {
        if (TryToGameBiz(key, out GameBiz gameBiz))
        {
            return gameBiz;
        }
        throw new ArgumentOutOfRangeException(nameof(key), key.ToString(), "Game key does not belong to the HoYo provider.");
    }


    /// <summary>
    /// 是否是已适配的游戏
    /// </summary>
    public static bool IsSupported(GameKey key)
    {
        return TryToGameBiz(key, out GameBiz gameBiz) && gameBiz.IsKnown();
    }


    #endregion



    #region 游戏专属常量


    /// <summary>
    /// 游戏进程名，带 .exe 扩展名。无法确定时返回 null，需要通过 HoYoPlay 接口查询。
    /// </summary>
    public static string? GetExecutableName(GameKey key)
    {
        if (!key.IsProvider(ProviderId))
        {
            return null;
        }
        // 原神国服与 Bilibili 服的进程名与国际服不同
        if (key.GameId is Hk4e)
        {
            return key.ChannelId is GameChannelIds.Global ? "GenshinImpact.exe" : "YuanShen.exe";
        }
        return key.GameId switch
        {
            Hkrpg => "StarRail.exe",
            Bh3 => "BH3.exe",
            Nap => "ZenlessZoneZero.exe",
            _ => null,
        };
    }


    /// <summary>
    /// 游戏截图目录，相对于游戏安装目录。无法确定时返回 null。
    /// </summary>
    public static string? GetScreenshotRelativePath(GameKey key)
    {
        if (!key.IsProvider(ProviderId))
        {
            return null;
        }
        return key.GameId switch
        {
            Hk4e => "ScreenShot",
            Hkrpg => @"StarRail_Data\ScreenShots",
            Bh3 => "ScreenShot",
            Nap => "ScreenShot",
            _ => null,
        };
    }


    /// <summary>
    /// 游戏图标
    /// </summary>
    public static string GetIconUri(GameKey key)
    {
        const string transparent = "ms-appx:///Assets/Image/Transparent.png";
        if (!key.IsProvider(ProviderId))
        {
            return transparent;
        }
        return key.GameId switch
        {
            Bh3 => "ms-appx:///Assets/Image/icon_bh3.jpg",
            Hk4e => "ms-appx:///Assets/Image/icon_ys.jpg",
            Hkrpg => "ms-appx:///Assets/Image/icon_sr.jpg",
            Nap => "ms-appx:///Assets/Image/icon_zzz.jpg",
            _ => transparent,
        };
    }


    /// <summary>
    /// 渠道图标
    /// </summary>
    public static string GetChannelIconUri(GameKey key)
    {
        return key.ChannelId switch
        {
            GameChannelIds.China => "ms-appx:///Assets/Image/gameicon_hyperion.png",
            GameChannelIds.Global => "ms-appx:///Assets/Image/gameicon_hoyolab.png",
            GameChannelIds.Bilibili => "ms-appx:///Assets/Image/gameicon_bilibili.png",
            _ => "ms-appx:///Assets/Image/Transparent.png",
        };
    }


    /// <summary>
    /// 游戏名称，已本地化
    /// </summary>
    public static string GetDisplayName(GameKey key)
    {
        return TryToGameBiz(key, out GameBiz gameBiz) ? gameBiz.ToGameName() : "";
    }


    /// <summary>
    /// 渠道名称，已本地化
    /// </summary>
    public static string GetChannelName(GameKey key)
    {
        return TryToGameBiz(key, out GameBiz gameBiz) ? gameBiz.ToGameServerName() : "";
    }


    /// <summary>
    /// 游戏设置所在的注册表键
    /// </summary>
    public static string GetGameRegistryKey(GameKey key)
    {
        return TryToGameBiz(key, out GameBiz gameBiz) ? gameBiz.GetGameRegistryKey() : "HKEY_CURRENT_USER";
    }


    #endregion



    #region 能力


    /// <summary>
    /// 所有米哈游游戏共有的能力
    /// </summary>
    private const GameCapability CommonCapabilities = GameCapability.Launch
                                                    | GameCapability.Discovery
                                                    | GameCapability.VersionCheck
                                                    | GameCapability.Install
                                                    | GameCapability.Update
                                                    | GameCapability.Repair
                                                    | GameCapability.PlayTime
                                                    | GameCapability.Announcement
                                                    | GameCapability.GameSetting
                                                    | GameCapability.Screenshot
                                                    | GameCapability.GameRecord
                                                    | GameCapability.InGameNotices;


    /// <summary>
    /// 原神、星穹铁道、绝区零共有的能力
    /// </summary>
    private const GameCapability GachaCapabilities = GameCapability.Gacha
                                                   | GameCapability.SelfQuery
                                                   | GameCapability.HardLink;


    /// <summary>
    /// 指定游戏支持的能力，未适配的游戏只支持启动
    /// </summary>
    public static GameCapability GetCapabilities(GameKey key)
    {
        if (!TryToGameBiz(key, out GameBiz gameBiz))
        {
            return GameCapability.None;
        }
        return gameBiz.Value switch
        {
            GameBiz.bh3_cn => CommonCapabilities
                            | GameCapability.AccountSwitcher
                            | GameCapability.DailyNote,

            GameBiz.bh3_global => CommonCapabilities
                                | GameCapability.DailyNote,

            GameBiz.hk4e_cn => CommonCapabilities
                             | GachaCapabilities
                             | GameCapability.BeyondGacha
                             | GameCapability.CloudGame
                             | GameCapability.AccountSwitcher
                             | GameCapability.DailyNote,

            GameBiz.hk4e_global => CommonCapabilities
                                 | GachaCapabilities
                                 | GameCapability.BeyondGacha
                                 | GameCapability.CloudGame
                                 | GameCapability.AccountSwitcher
                                 | GameCapability.DailyNote,

            GameBiz.hk4e_bilibili => CommonCapabilities
                                   | GachaCapabilities
                                   | GameCapability.BeyondGacha
                                   | GameCapability.DailyNote,

            GameBiz.hkrpg_cn => CommonCapabilities
                              | GachaCapabilities
                              | GameCapability.AccountSwitcher
                              | GameCapability.DailyNote,

            GameBiz.hkrpg_global => CommonCapabilities
                                  | GachaCapabilities
                                  | GameCapability.AccountSwitcher
                                  | GameCapability.DailyNote,

            GameBiz.hkrpg_bilibili => CommonCapabilities
                                    | GachaCapabilities
                                    | GameCapability.DailyNote,

            GameBiz.nap_cn => CommonCapabilities
                            | GachaCapabilities
                            | GameCapability.CloudGame
                            | GameCapability.AccountSwitcher
                            | GameCapability.DailyNote,

            GameBiz.nap_global => CommonCapabilities
                                | GachaCapabilities
                                | GameCapability.AccountSwitcher
                                | GameCapability.DailyNote,

            GameBiz.nap_bilibili => CommonCapabilities
                                  | GachaCapabilities
                                  | GameCapability.DailyNote,

            // HoYoPlay 接口返回但未适配的游戏，只允许启动
            _ => GameCapability.Launch,
        };
    }


    #endregion

}
