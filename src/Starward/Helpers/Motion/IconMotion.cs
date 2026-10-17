using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using System;
using System.Numerics;

namespace Starward.Helpers;

/// <summary>
/// 导航栏字体图标的小动作，仿照设置齿轮的 AnimatedIcon：
/// 指针移上去摆一个姿势、按下时收一下、点下去后做一次和图标形状相关的动作。
/// 内置的 AnimatedIcon 只有少数几个 Lottie 图标，其他图标只能用 Composition 自己动。
/// </summary>
public static class IconMotion
{

    private static readonly ILogger _logger = AppConfig.GetLogger<IconMotionLog>();

    private sealed class IconMotionLog { }


    /// <summary>
    /// 一个姿势：旋转角度、上下位移、缩放
    /// </summary>
    public readonly record struct Pose(float Rotation = 0, float OffsetX = 0, float OffsetY = 0, float Scale = 1)
    {
        /// <summary>
        /// 静止的姿势。不能用 default，那会把缩放也设成 0，图标就缩没了
        /// </summary>
        public static readonly Pose Rest = new(Rotation: 0);
    }


    /// <summary>
    /// 一个图标的整套动作
    /// </summary>
    public sealed record Style(Pose Hover, Pose Pressed, Action<Visual>? Invoke);



    /// <summary>
    /// 给导航项的字体图标挂上动作
    /// </summary>
    public static void Attach(NavigationViewItem item, Style style)
    {
        if (item.Icon is not IconElement icon)
        {
            return;
        }
        bool pointerOver = false;
        item.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler((_, _) =>
        {
            pointerOver = true;
            To(icon, style.Hover);
        }), true);
        item.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((_, _) =>
        {
            pointerOver = false;
            To(icon, Pose.Rest);
        }), true);
        item.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, _) =>
        {
            pointerOver = false;
            To(icon, Pose.Rest);
        }), true);
        item.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => To(icon, style.Pressed)), true);
        item.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) =>
        {
            To(icon, pointerOver ? style.Hover : Pose.Rest);
            // 一次性动作会盖掉姿势，做完再摆回当下该有的姿势
            Play(icon, style.Invoke, () => To(icon, pointerOver ? style.Hover : Pose.Rest));
        }), true);
    }



    /// <summary>
    /// 用 spring 摆到一个姿势
    /// </summary>
    private static void To(IconElement icon, Pose pose)
    {
        if (!MotionSettings.IsEnabled)
        {
            return;
        }
        try
        {
            Visual visual = Prepare(icon);
            Compositor compositor = visual.Compositor;

            SpringScalarNaturalMotionAnimation rotation = compositor.CreateSpringScalarAnimation();
            rotation.FinalValue = pose.Rotation;
            rotation.DampingRatio = 0.55f;
            rotation.Period = TimeSpan.FromMilliseconds(45);
            visual.StartAnimation(nameof(Visual.RotationAngleInDegrees), rotation);

            SpringVector3NaturalMotionAnimation offset = compositor.CreateSpringVector3Animation();
            offset.FinalValue = new Vector3(pose.OffsetX, pose.OffsetY, 0);
            offset.DampingRatio = 0.6f;
            offset.Period = TimeSpan.FromMilliseconds(45);
            visual.StartAnimation("Translation", offset);

            SpringVector3NaturalMotionAnimation scale = compositor.CreateSpringVector3Animation();
            scale.FinalValue = new Vector3(pose.Scale, pose.Scale, 1);
            scale.DampingRatio = 0.6f;
            scale.Period = TimeSpan.FromMilliseconds(45);
            visual.StartAnimation(nameof(Visual.Scale), scale);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Icon pose");
        }
    }


    private static void Play(IconElement icon, Action<Visual>? invoke, Action completed)
    {
        if (invoke is null || !MotionSettings.IsEnabled)
        {
            return;
        }
        try
        {
            Visual visual = Prepare(icon);
            CompositionScopedBatch batch = visual.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            invoke(visual);
            batch.End();
            batch.Completed += (_, _) => completed();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Icon invoke");
        }
    }


    private static Visual Prepare(IconElement icon)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(icon, true);
        Visual visual = ElementCompositionPreview.GetElementVisual(icon);
        Vector2 size = icon.ActualSize;
        visual.CenterPoint = new Vector3(size.X / 2, size.Y / 2, 0);
        return visual;
    }



    #region 点击后的一次性动作


    /// <summary>
    /// 旋转按关键帧摆动，例如左右摇晃
    /// </summary>
    public static Action<Visual> Wiggle(params float[] angles) => visual =>
    {
        Compositor compositor = visual.Compositor;
        ScalarKeyFrameAnimation animation = compositor.CreateScalarKeyFrameAnimation();
        for (int i = 0; i < angles.Length; i++)
        {
            animation.InsertKeyFrame((i + 1f) / (angles.Length + 1), angles[i]);
        }
        animation.InsertKeyFrame(1, 0);
        animation.Duration = TimeSpan.FromMilliseconds(90 * (angles.Length + 1));
        visual.StartAnimation(nameof(Visual.RotationAngleInDegrees), animation);
    };


    /// <summary>
    /// 往上跳，可以连跳几下，每下比前一下低
    /// </summary>
    public static Action<Visual> Jump(float height, int times = 1) => visual =>
    {
        Compositor compositor = visual.Compositor;
        Vector3KeyFrameAnimation animation = compositor.CreateVector3KeyFrameAnimation();
        for (int i = 0; i < times; i++)
        {
            float start = (float)i / times;
            float peak = start + 0.5f / times;
            float end = (i + 1f) / times;
            float h = height * MathF.Pow(0.6f, i);
            animation.InsertKeyFrame(peak, new Vector3(0, -h, 0), MotionTokens.Decelerate(compositor));
            animation.InsertKeyFrame(end, Vector3.Zero, MotionTokens.Accelerate(compositor));
        }
        animation.Duration = TimeSpan.FromMilliseconds(320 * times);
        visual.StartAnimation("Translation", animation);
    };


    /// <summary>
    /// 转一圈的若干分之一，同时脉动一下
    /// </summary>
    public static Action<Visual> SpinPulse(float degrees) => visual =>
    {
        Compositor compositor = visual.Compositor;
        ScalarKeyFrameAnimation spin = compositor.CreateScalarKeyFrameAnimation();
        spin.InsertKeyFrame(0, 0);
        spin.InsertKeyFrame(1, degrees, MotionTokens.Standard(compositor));
        spin.Duration = TimeSpan.FromMilliseconds(500);
        visual.StartAnimation(nameof(Visual.RotationAngleInDegrees), spin);

        Vector3KeyFrameAnimation pulse = compositor.CreateVector3KeyFrameAnimation();
        pulse.InsertKeyFrame(0.4f, new Vector3(1.25f, 1.25f, 1), MotionTokens.Decelerate(compositor));
        pulse.InsertKeyFrame(1, Vector3.One, MotionTokens.Standard(compositor));
        pulse.Duration = TimeSpan.FromMilliseconds(500);
        visual.StartAnimation(nameof(Visual.Scale), pulse);
    };


    /// <summary>
    /// 往右滑出一点再回来
    /// </summary>
    public static Action<Visual> Slide(float distance) => visual =>
    {
        Compositor compositor = visual.Compositor;
        Vector3KeyFrameAnimation animation = compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0.4f, new Vector3(distance, 0, 0), MotionTokens.Decelerate(compositor));
        animation.InsertKeyFrame(1, Vector3.Zero, MotionTokens.Standard(compositor));
        animation.Duration = TimeSpan.FromMilliseconds(420);
        visual.StartAnimation("Translation", animation);
    };


    #endregion


}
