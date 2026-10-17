using Microsoft.UI.Composition;
using System;
using System.Numerics;

namespace Starward.Helpers;

/// <summary>
/// 统一的动画时长、曲线和模糊量，新增动画都从这里取值
/// </summary>
public static class MotionTokens
{

    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(150);

    public static readonly TimeSpan Normal = TimeSpan.FromMilliseconds(250);

    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(400);

    public static readonly TimeSpan Emphasized = TimeSpan.FromMilliseconds(450);

    /// <summary>
    /// 错落进场时相邻元素的间隔
    /// </summary>
    public static readonly TimeSpan StaggerStep = TimeSpan.FromMilliseconds(35);

    /// <summary>
    /// 错落进场最多延迟到第几个元素，之后的元素和它同时进场
    /// </summary>
    public const int StaggerMax = 8;


    /// <summary>
    /// 只是让内容由糊变清的轻度模糊
    /// </summary>
    public const float BlurSubtle = 6;

    public const float BlurMedium = 16;

    /// <summary>
    /// 毛玻璃底的模糊量
    /// </summary>
    public const float BlurHeavy = 30;


    /// <summary>
    /// Spring 的阻尼和周期，接近 iOS 的手感：快速到位、轻微回弹
    /// </summary>
    public const float SpringDampingRatio = 0.78f;

    public static readonly TimeSpan SpringPeriod = TimeSpan.FromMilliseconds(50);


    /// <summary>
    /// 进场用的减速曲线
    /// </summary>
    public static CompositionEasingFunction Decelerate(Compositor compositor)
    {
        return compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));
    }


    /// <summary>
    /// 来回切换用的标准曲线
    /// </summary>
    public static CompositionEasingFunction Standard(Compositor compositor)
    {
        return compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0f), new Vector2(0f, 1f));
    }


    /// <summary>
    /// 退场用的加速曲线
    /// </summary>
    public static CompositionEasingFunction Accelerate(Compositor compositor)
    {
        return compositor.CreateCubicBezierEasingFunction(new Vector2(0.7f, 0f), new Vector2(1f, 0.5f));
    }


}
