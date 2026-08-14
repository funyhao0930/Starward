using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Core.HoYoPlay;
using Starward.Features.Gacha;
using Starward.Features.GameLauncher;
using Starward.Features.GameRecord;
using Starward.Features.GameSetting;
using Starward.Features.Screenshot;
using Starward.Features.SelfQuery;
using System.Collections.Generic;

namespace Starward.Features;

/// <summary>
/// 单款游戏可以使用的功能。
/// 不再按 GameBiz 逐个硬编码，而是由 <see cref="GameDescriptor.Capabilities"/> 推导，
/// 新增游戏时只需要在对应的 Provider 中声明能力。
/// </summary>
internal partial class GameFeatureConfig
{


    private GameFeatureConfig()
    {

    }


    /// <summary>
    /// 支持的页面
    /// </summary>
    public List<string> SupportedPages { get; init; } = [];


    /// <summary>
    /// 游戏内通知内容
    /// </summary>
    public bool InGameNoticesWindow { get; init; }


    /// <summary>
    /// 支持硬链接
    /// </summary>
    public bool SupportHardLink { get; init; }


    /// <summary>
    /// 支持云游戏
    /// </summary>
    public bool SupportCloudGame { get; init; }


    /// <summary>
    /// 支持游戏账号切换
    /// </summary>
    public bool SupportGameAccountSwitcher { get; init; }


    /// <summary>
    /// 支持实时便笺
    /// </summary>
    public bool SupportDailyNote { get; init; }



    /// <summary>
    /// 没有选择游戏时使用
    /// </summary>
    public static GameFeatureConfig None { get; } = new();



    /// <summary>
    /// 由游戏描述推导可以使用的功能
    /// </summary>
    public static GameFeatureConfig FromDescriptor(GameDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            return None;
        }
        return FromCapabilities(descriptor.Capabilities);
    }



    /// <summary>
    /// 由能力标志推导可以使用的功能
    /// </summary>
    public static GameFeatureConfig FromCapabilities(GameCapability capabilities)
    {
        if (capabilities is GameCapability.None)
        {
            return None;
        }
        var pages = new List<string>();
        AddPageIf(pages, capabilities, GameCapability.Launch, nameof(GameLauncherPage));
        AddPageIf(pages, capabilities, GameCapability.GameSetting, nameof(GameSettingPage));
        AddPageIf(pages, capabilities, GameCapability.Screenshot, nameof(ScreenshotPage));
        AddPageIf(pages, capabilities, GameCapability.Gacha, nameof(GachaLogPage));
        AddPageIf(pages, capabilities, GameCapability.GameRecord, nameof(GameRecordPage));
        AddPageIf(pages, capabilities, GameCapability.SelfQuery, nameof(SelfQueryPage));
        AddPageIf(pages, capabilities, GameCapability.BeyondGacha, nameof(GenshinBeyondGachaPage));
        return new GameFeatureConfig
        {
            SupportedPages = pages,
            InGameNoticesWindow = capabilities.HasFlag(GameCapability.InGameNotices),
            SupportHardLink = capabilities.HasFlag(GameCapability.HardLink),
            SupportCloudGame = capabilities.HasFlag(GameCapability.CloudGame),
            SupportGameAccountSwitcher = capabilities.HasFlag(GameCapability.AccountSwitcher),
            SupportDailyNote = capabilities.HasFlag(GameCapability.DailyNote),
        };
    }


    private static void AddPageIf(List<string> pages, GameCapability capabilities, GameCapability required, string pageName)
    {
        if (capabilities.HasFlag(required))
        {
            pages.Add(pageName);
        }
    }



    /// <summary>
    /// 由游戏标识查出可以使用的功能。
    /// <para/>
    /// 本方法是静态的，无法使用构造函数注入，因此沿用应用既有的
    /// <see cref="AppConfig.GetService{T}"/>；这是本类型唯一一处。
    /// </summary>
    public static GameFeatureConfig FromGameKey(GameKey key)
    {
        if (!key.IsValid)
        {
            return None;
        }
        GameDescriptor? descriptor = AppConfig.GetService<IGameProviderRegistry>().GetGame(key);
        // 供应商接口返回但尚未适配的游戏，只允许启动
        return FromCapabilities(descriptor?.Capabilities ?? GameCapability.Launch);
    }


}
