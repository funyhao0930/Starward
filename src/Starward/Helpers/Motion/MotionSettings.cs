using System;
using Windows.UI.ViewManagement;

namespace Starward.Helpers;

/// <summary>
/// 介面动画的总开关，同时跟随应用设置和系统的「动画效果」「透明效果」
/// </summary>
public static class MotionSettings
{

    private static readonly UISettings _uiSettings = new();


    /// <summary>
    /// 是否播放 Composition 动画，关闭时所有动画直接跳到终点
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                return AppConfig.EnableUIAnimation && _uiSettings.AnimationsEnabled;
            }
            catch
            {
                return AppConfig.EnableUIAnimation;
            }
        }
    }


    /// <summary>
    /// 系统是否开启透明效果，关闭时模糊笔刷改用纯色
    /// </summary>
    public static bool IsTransparencyEnabled
    {
        get
        {
            try
            {
                return _uiSettings.AdvancedEffectsEnabled;
            }
            catch
            {
                return true;
            }
        }
    }


    /// <summary>
    /// 是否在动画中使用模糊，模糊只是过渡的媒介，动画关闭时也不需要
    /// </summary>
    public static bool IsBlurEnabled => IsEnabled && IsTransparencyEnabled;


    /// <summary>
    /// 动画关闭时返回 0，用于和动画配合的等待
    /// </summary>
    public static TimeSpan Scale(TimeSpan duration) => IsEnabled ? duration : TimeSpan.Zero;


}
