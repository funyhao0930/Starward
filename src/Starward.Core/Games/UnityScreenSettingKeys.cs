namespace Starward.Core.Games;

/// <summary>
/// Unity 引擎自己写入注册表的屏幕设置键名。
/// <para/>
/// 名字后面的散列由 Unity 生成，与游戏无关，每个 Unity 游戏都是这一组，
/// 因此读写它们不涉及任何厂商私有格式。
/// </summary>
public static class UnityScreenSettingKeys
{

    public const string ResolutionWidth = "Screenmanager Resolution Width_h182942802";

    public const string ResolutionHeight = "Screenmanager Resolution Height_h2627697771";

    /// <summary>
    /// 1 全屏，3 窗口
    /// </summary>
    public const string FullscreenMode = "Screenmanager Fullscreen mode_h3630240806";


    public const int FullScreenValue = 1;

    public const int WindowedValue = 3;

}
