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
    /// 兼容层：现有调用方仍以 <see cref="GameId"/> 为货币。
    /// 本方法是静态的，无法使用构造函数注入，因此沿用应用既有的 <see cref="AppConfig.GetService{T}"/>；
    /// 新代码应直接使用 <see cref="FromDescriptor(GameDescriptor?)"/>。
    /// </summary>
    public static GameFeatureConfig FromGameId(GameId? gameId)
    {
        if (gameId is null)
        {
            return None;
        }
        // 必须用 GameKeyResolver：非米哈游游戏的键是 GameKey 的正规字符串，
        // 用 HoYo 的映射会解析失败，导致这些游戏只剩启动页
        if (GameKeyResolver.Resolve(gameId.GameBiz.Value) is not GameKey key)
        {
            // 无法识别的键，与重构前一致，只允许启动
            return FromCapabilities(GameCapability.Launch);
        }
        GameDescriptor? descriptor = AppConfig.GetService<IGameProviderRegistry>().GetGame(key);
        // HoYoPlay 接口返回但尚未适配的游戏，只允许启动
        return FromCapabilities(descriptor?.Capabilities ?? GameCapability.Launch);
    }


}
