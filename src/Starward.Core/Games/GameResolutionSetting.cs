namespace Starward.Core.Games;

/// <summary>
/// 游戏的分辨率与窗口模式，各家存法不同，这是共同的部分。
/// </summary>
/// <param name="Width">水平分辨率</param>
/// <param name="Height">垂直分辨率</param>
/// <param name="FullScreen">是否全屏</param>
public readonly record struct GameResolutionSetting(int Width, int Height, bool FullScreen);
