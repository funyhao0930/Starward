using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Core.HoYoPlay;
using Xunit;

namespace Starward.Core.Tests.Games;

/// <summary>
/// 重构回归测试：现有 4 款米哈游游戏的映射必须与重构前完全一致。
/// 期望值全部以字面量写死，作为黄金基准。
/// </summary>
public class HoYoGameMappingTests
{

    /// <summary>
    /// 与重构前 GameFeatureConfig 有对应关系的能力。
    /// 其余能力（Discovery、Install、Update 等）在重构前没有表示，不参与比较。
    /// </summary>
    private const GameCapability FeatureConfigMask = GameCapability.Launch
                                                   | GameCapability.GameSetting
                                                   | GameCapability.Screenshot
                                                   | GameCapability.Gacha
                                                   | GameCapability.GameRecord
                                                   | GameCapability.SelfQuery
                                                   | GameCapability.BeyondGacha
                                                   | GameCapability.InGameNotices
                                                   | GameCapability.HardLink
                                                   | GameCapability.CloudGame
                                                   | GameCapability.AccountSwitcher
                                                   | GameCapability.DailyNote;


    /// <summary>
    /// 重构前每个 GameBiz 支持的功能，逐字对应 GameFeatureConfig 中的 11 个静态实例
    /// </summary>
    public static TheoryData<string, GameCapability> ExpectedFeatureCapabilities => new()
    {
        // 页面：+ GachaLog、SelfQuery、GenshinBeyondGacha
        // 布尔：+ SupportHardLink、SupportCloudGame
        {
            GameBiz.hk4e_cn,
            GameCapability.Launch | GameCapability.GameSetting | GameCapability.Screenshot | GameCapability.GameRecord
            | GameCapability.Gacha | GameCapability.SelfQuery | GameCapability.BeyondGacha
            | GameCapability.InGameNotices | GameCapability.HardLink | GameCapability.CloudGame
            | GameCapability.AccountSwitcher | GameCapability.DailyNote
        },
        {
            GameBiz.hk4e_global,
            GameCapability.Launch | GameCapability.GameSetting | GameCapability.Screenshot | GameCapability.GameRecord
            | GameCapability.Gacha | GameCapability.SelfQuery | GameCapability.BeyondGacha
            | GameCapability.InGameNotices | GameCapability.HardLink | GameCapability.CloudGame
            | GameCapability.AccountSwitcher | GameCapability.DailyNote
        },
        {
            GameBiz.hk4e_bilibili,
            GameCapability.Launch | GameCapability.GameSetting | GameCapability.Screenshot | GameCapability.GameRecord
            | GameCapability.Gacha | GameCapability.SelfQuery | GameCapability.BeyondGacha
            | GameCapability.InGameNotices | GameCapability.HardLink | GameCapability.DailyNote
        },
        {
            GameBiz.hkrpg_cn,
            GameCapability.Launch | GameCapability.GameSetting | GameCapability.Screenshot | GameCapability.GameRecord
            | GameCapability.Gacha | GameCapability.SelfQuery
            | GameCapability.InGameNotices | GameCapability.HardLink
            | GameCapability.AccountSwitcher | GameCapability.DailyNote
        },
        {
            GameBiz.hkrpg_global,
            GameCapability.Launch | GameCapability.GameSetting | GameCapability.Screenshot | GameCapability.GameRecord
            | GameCapability.Gacha | GameCapability.SelfQuery
            | GameCapability.InGameNotices | GameCapability.HardLink
            | GameCapability.AccountSwitcher | GameCapability.DailyNote
        },
        {
            GameBiz.hkrpg_bilibili,
            GameCapability.Launch | GameCapability.GameSetting | GameCapability.Screenshot | GameCapability.GameRecord
            | GameCapability.Gacha | GameCapability.SelfQuery
            | GameCapability.InGameNotices | GameCapability.HardLink | GameCapability.DailyNote
        },
        {
            GameBiz.nap_cn,
            GameCapability.Launch | GameCapability.GameSetting | GameCapability.Screenshot | GameCapability.GameRecord
            | GameCapability.Gacha | GameCapability.SelfQuery
            | GameCapability.InGameNotices | GameCapability.HardLink | GameCapability.CloudGame
            | GameCapability.AccountSwitcher | GameCapability.DailyNote
        },
        {
            GameBiz.nap_global,
            GameCapability.Launch | GameCapability.GameSetting | GameCapability.Screenshot | GameCapability.GameRecord
            | GameCapability.Gacha | GameCapability.SelfQuery
            | GameCapability.InGameNotices | GameCapability.HardLink
            | GameCapability.AccountSwitcher | GameCapability.DailyNote
        },
        {
            GameBiz.nap_bilibili,
            GameCapability.Launch | GameCapability.GameSetting | GameCapability.Screenshot | GameCapability.GameRecord
            | GameCapability.Gacha | GameCapability.SelfQuery
            | GameCapability.InGameNotices | GameCapability.HardLink | GameCapability.DailyNote
        },
    };


    [Theory]
    [MemberData(nameof(ExpectedFeatureCapabilities))]
    public void GetCapabilities_MatchesPreRefactorFeatureConfig(string biz, GameCapability expected)
    {
        GameKey key = HoYoGameMapping.FromGameBiz(biz);
        GameCapability actual = HoYoGameMapping.GetCapabilities(key) & FeatureConfigMask;
        Assert.Equal(expected, actual);
    }


    /// <summary>
    /// 所有已适配的米哈游游戏都支持这些基础能力
    /// </summary>
    [Fact]
    public void AllSupportedGames_HaveBaseCapabilities()
    {
        const GameCapability baseCapabilities = GameCapability.Launch
                                              | GameCapability.Discovery
                                              | GameCapability.VersionCheck
                                              | GameCapability.Install
                                              | GameCapability.Update
                                              | GameCapability.Repair
                                              | GameCapability.PlayTime
                                              | GameCapability.Announcement;
        foreach (GameKey key in HoYoGameMapping.SupportedGameKeys)
        {
            Assert.Equal(baseCapabilities, HoYoGameMapping.GetCapabilities(key) & baseCapabilities);
        }
    }


    /// <summary>
    /// 未适配的游戏只允许启动
    /// </summary>
    [Fact]
    public void GetCapabilities_UnadaptedGame_LaunchOnly()
    {
        Assert.Equal(GameCapability.Launch, HoYoGameMapping.GetCapabilities(HoYoGameMapping.FromGameBiz("hk4e_os")));
    }


    /// <summary>
    /// 不属于本供应商的游戏没有任何能力
    /// </summary>
    [Fact]
    public void GetCapabilities_OtherProvider_None()
    {
        Assert.Equal(GameCapability.None, HoYoGameMapping.GetCapabilities(new GameKey("kuro", "wuwa", "cn")));
    }



    [Theory]
    [InlineData(GameBiz.hk4e_cn, "YuanShen.exe")]
    [InlineData(GameBiz.hk4e_bilibili, "YuanShen.exe")]
    [InlineData(GameBiz.hk4e_global, "GenshinImpact.exe")]
    [InlineData(GameBiz.hkrpg_cn, "StarRail.exe")]
    [InlineData(GameBiz.hkrpg_global, "StarRail.exe")]
    [InlineData(GameBiz.hkrpg_bilibili, "StarRail.exe")]
    [InlineData(GameBiz.nap_cn, "ZenlessZoneZero.exe")]
    [InlineData(GameBiz.nap_global, "ZenlessZoneZero.exe")]
    [InlineData(GameBiz.nap_bilibili, "ZenlessZoneZero.exe")]
    public void GetExecutableName_MatchesPreRefactorSwitch(string biz, string expected)
    {
        Assert.Equal(expected, HoYoGameMapping.GetExecutableName(HoYoGameMapping.FromGameBiz(biz)));
    }


    [Fact]
    public void GetExecutableName_ReturnsNullForUnknownGame()
    {
        Assert.Null(HoYoGameMapping.GetExecutableName(HoYoGameMapping.FromGameBiz("clgm_cn")));
        Assert.Null(HoYoGameMapping.GetExecutableName(new GameKey("kuro", "wuwa", "cn")));
    }



    /// <summary>
    /// HoYoPlay 的远程 GameId 不能变，否则所有接口都会失效
    /// </summary>
    [Theory]
    [InlineData(GameBiz.bh3_cn, "osvnlOc0S8")]
    [InlineData(GameBiz.bh3_global, "5TIVvvcwtM")]
    [InlineData(GameBiz.hk4e_cn, "1Z8W5NHUQb")]
    [InlineData(GameBiz.hk4e_global, "gopR6Cufr3")]
    [InlineData(GameBiz.hk4e_bilibili, "T2S0Gz4Dr2")]
    [InlineData(GameBiz.hkrpg_cn, "64kMb5iAWu")]
    [InlineData(GameBiz.hkrpg_global, "4ziysqXOQ8")]
    [InlineData(GameBiz.hkrpg_bilibili, "EdtUqXfCHh")]
    [InlineData(GameBiz.nap_cn, "x6znKlJ0xK")]
    [InlineData(GameBiz.nap_global, "U5hbdsT9W7")]
    [InlineData(GameBiz.nap_bilibili, "HXAFlmYa17")]
    public void HoYoPlayGameId_Unchanged(string biz, string expectedId)
    {
        GameId? gameId = GameId.FromGameBiz(biz);
        Assert.NotNull(gameId);
        Assert.Equal(expectedId, gameId.Id);
        Assert.Equal(biz, gameId.GameBiz.Value);
    }


    [Theory]
    [InlineData(GameBiz.bh3_cn, LauncherId.ChinaOfficial)]
    [InlineData(GameBiz.hk4e_cn, LauncherId.ChinaOfficial)]
    [InlineData(GameBiz.hkrpg_cn, LauncherId.ChinaOfficial)]
    [InlineData(GameBiz.nap_cn, LauncherId.ChinaOfficial)]
    [InlineData(GameBiz.bh3_global, LauncherId.GlobalOfficial)]
    [InlineData(GameBiz.hk4e_global, LauncherId.GlobalOfficial)]
    [InlineData(GameBiz.hkrpg_global, LauncherId.GlobalOfficial)]
    [InlineData(GameBiz.nap_global, LauncherId.GlobalOfficial)]
    [InlineData(GameBiz.hk4e_bilibili, LauncherId.BilibiliGenshin)]
    [InlineData(GameBiz.hkrpg_bilibili, LauncherId.BilibiliStarRail)]
    [InlineData(GameBiz.nap_bilibili, LauncherId.BilibiliZZZ)]
    public void LauncherId_Unchanged(string biz, string expectedLauncherId)
    {
        Assert.Equal(expectedLauncherId, LauncherId.FromGameBiz(biz));
    }


    /// <summary>
    /// LauncherId 与远程 GameId 只存在于 HoYo 供应商内部，不进入通用的 GameKey
    /// </summary>
    [Fact]
    public void GameKey_DoesNotContainHoYoPlayIdentifiers()
    {
        foreach (GameKey key in HoYoGameMapping.SupportedGameKeys)
        {
            string text = key.ToString();
            GameId gameId = GameId.FromGameBiz(HoYoGameMapping.ToGameBiz(key))!;
            Assert.DoesNotContain(gameId.Id, text, StringComparison.Ordinal);
            Assert.DoesNotContain(LauncherId.FromGameBiz(HoYoGameMapping.ToGameBiz(key))!, text, StringComparison.Ordinal);
        }
    }



    [Theory]
    [InlineData(GameBiz.hk4e_cn, "ScreenShot")]
    [InlineData(GameBiz.hkrpg_cn, @"StarRail_Data\ScreenShots")]
    [InlineData(GameBiz.bh3_cn, "ScreenShot")]
    [InlineData(GameBiz.nap_cn, "ScreenShot")]
    public void GetScreenshotRelativePath_MatchesPreRefactorSwitch(string biz, string expected)
    {
        Assert.Equal(expected, HoYoGameMapping.GetScreenshotRelativePath(HoYoGameMapping.FromGameBiz(biz)));
    }


    [Theory]
    [InlineData(GameBiz.bh3_cn, "ms-appx:///Assets/Image/icon_bh3.jpg")]
    [InlineData(GameBiz.hk4e_cn, "ms-appx:///Assets/Image/icon_ys.jpg")]
    [InlineData(GameBiz.hkrpg_cn, "ms-appx:///Assets/Image/icon_sr.jpg")]
    [InlineData(GameBiz.nap_cn, "ms-appx:///Assets/Image/icon_zzz.jpg")]
    public void GetIconUri_MatchesPreRefactorSwitch(string biz, string expected)
    {
        Assert.Equal(expected, HoYoGameMapping.GetIconUri(HoYoGameMapping.FromGameBiz(biz)));
    }


    [Theory]
    [InlineData(GameBiz.hk4e_cn, "ms-appx:///Assets/Image/gameicon_hyperion.png")]
    [InlineData(GameBiz.hk4e_global, "ms-appx:///Assets/Image/gameicon_hoyolab.png")]
    [InlineData(GameBiz.hk4e_bilibili, "ms-appx:///Assets/Image/gameicon_bilibili.png")]
    public void GetChannelIconUri_MatchesPreRefactorSwitch(string biz, string expected)
    {
        Assert.Equal(expected, HoYoGameMapping.GetChannelIconUri(HoYoGameMapping.FromGameBiz(biz)));
    }


    /// <summary>
    /// 游戏设置所在的注册表键不能变
    /// </summary>
    [Theory]
    [InlineData(GameBiz.hk4e_cn, @"HKEY_CURRENT_USER\Software\miHoYo\原神")]
    [InlineData(GameBiz.hk4e_bilibili, @"HKEY_CURRENT_USER\Software\miHoYo\原神")]
    [InlineData(GameBiz.hk4e_global, @"HKEY_CURRENT_USER\Software\miHoYo\Genshin Impact")]
    [InlineData(GameBiz.hkrpg_cn, @"HKEY_CURRENT_USER\Software\miHoYo\崩坏：星穹铁道")]
    [InlineData(GameBiz.hkrpg_global, @"HKEY_CURRENT_USER\Software\Cognosphere\Star Rail")]
    [InlineData(GameBiz.bh3_cn, @"HKEY_CURRENT_USER\Software\miHoYo\崩坏3")]
    [InlineData(GameBiz.bh3_global, @"HKEY_CURRENT_USER\Software\miHoYo\Honkai Impact 3rd")]
    [InlineData(GameBiz.nap_cn, @"HKEY_CURRENT_USER\Software\miHoYo\绝区零")]
    [InlineData(GameBiz.nap_global, @"HKEY_CURRENT_USER\Software\miHoYo\ZenlessZoneZero")]
    public void GetGameRegistryKey_MatchesPreRefactorSwitch(string biz, string expected)
    {
        Assert.Equal(expected, HoYoGameMapping.GetGameRegistryKey(HoYoGameMapping.FromGameBiz(biz)));
    }

}
