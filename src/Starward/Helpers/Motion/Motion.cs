using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.UI;

namespace Starward.Helpers;

/// <summary>
/// 元素的进场、退场和按压动画。
/// 全部作用在元素的 Composition Visual 上（Opacity、Translation、Scale），
/// 和 XAML 的 Opacity、Translation 属性互不干扰，所以不要和 ScalarTransition 用在同一个元素上。
/// </summary>
public static class Motion
{


    /// <summary>
    /// 进场：淡入、从下方滑入、可选从略小放大到原尺寸、可选由糊变清
    /// </summary>
    /// <param name="offsetY">起始的 Y 偏移，正数表示从下方进入</param>
    /// <param name="fromScale">起始缩放，1 表示不缩放</param>
    /// <param name="blur">起始模糊量，0 表示不模糊</param>
    public static Task EnterAsync(this UIElement element, float offsetY = 12, float fromScale = 1, float blur = 0, TimeSpan delay = default, TimeSpan? duration = null)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        if (!MotionSettings.IsEnabled)
        {
            ResetMotion(element);
            return Task.CompletedTask;
        }
        Compositor compositor = visual.Compositor;
        TimeSpan time = duration ?? MotionTokens.Slow;
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        ScalarKeyFrameAnimation opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0, 0);
        opacity.InsertKeyFrame(1, 1, MotionTokens.Decelerate(compositor));
        // 淡入比位移快一点，元素先出现再落位，看起来更轻
        opacity.Duration = time * 0.7;
        opacity.DelayTime = delay;
        opacity.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        visual.StartAnimation(nameof(Visual.Opacity), opacity);

        if (offsetY != 0)
        {
            Vector3KeyFrameAnimation translation = compositor.CreateVector3KeyFrameAnimation();
            translation.InsertKeyFrame(0, new Vector3(0, offsetY, 0));
            translation.InsertKeyFrame(1, Vector3.Zero, MotionTokens.Decelerate(compositor));
            translation.Duration = time;
            translation.DelayTime = delay;
            translation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            visual.StartAnimation("Translation", translation);
        }

        if (fromScale != 1)
        {
            EnsureCenterPoint(element);
            Vector3KeyFrameAnimation scale = compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(0, new Vector3(fromScale, fromScale, 1));
            scale.InsertKeyFrame(1, Vector3.One, MotionTokens.Decelerate(compositor));
            scale.Duration = time;
            scale.DelayTime = delay;
            scale.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            visual.StartAnimation(nameof(Visual.Scale), scale);
        }

        if (blur > 0 && MotionSettings.IsBlurEnabled)
        {
            BlurLayer.Animate(element, blur, 0, time, delay);
        }

        batch.End();
        return batch.AsTask();
    }



    /// <summary>
    /// 退场：淡出，可选位移、缩放、模糊，结束后元素保持透明，由调用方决定是否折叠
    /// </summary>
    public static Task ExitAsync(this UIElement element, float offsetY = 0, float toScale = 1, float blur = 0, TimeSpan? duration = null)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        if (!MotionSettings.IsEnabled)
        {
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.Opacity = 0;
            return Task.CompletedTask;
        }
        Compositor compositor = visual.Compositor;
        TimeSpan time = duration ?? MotionTokens.Fast;
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        ScalarKeyFrameAnimation opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1, 0, MotionTokens.Accelerate(compositor));
        opacity.Duration = time;
        visual.StartAnimation(nameof(Visual.Opacity), opacity);

        if (offsetY != 0)
        {
            Vector3KeyFrameAnimation translation = compositor.CreateVector3KeyFrameAnimation();
            translation.InsertKeyFrame(1, new Vector3(0, offsetY, 0), MotionTokens.Accelerate(compositor));
            translation.Duration = time;
            visual.StartAnimation("Translation", translation);
        }

        if (toScale != 1)
        {
            EnsureCenterPoint(element);
            Vector3KeyFrameAnimation scale = compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(1, new Vector3(toScale, toScale, 1), MotionTokens.Accelerate(compositor));
            scale.Duration = time;
            visual.StartAnimation(nameof(Visual.Scale), scale);
        }

        if (blur > 0 && MotionSettings.IsBlurEnabled)
        {
            BlurLayer.Animate(element, 0, blur, time, TimeSpan.Zero);
        }

        batch.End();
        return batch.AsTask();
    }



    /// <summary>
    /// 一组元素依次进场，第 <see cref="MotionTokens.StaggerMax"/> 个之后不再增加延迟
    /// </summary>
    public static Task StaggerInAsync(this IEnumerable<UIElement> elements, float offsetY = 16, float fromScale = 1, float blur = 0, TimeSpan delay = default)
    {
        var tasks = new List<Task>();
        int index = 0;
        foreach (UIElement element in elements)
        {
            TimeSpan itemDelay = delay + MotionTokens.StaggerStep * Math.Min(index, MotionTokens.StaggerMax);
            tasks.Add(element.EnterAsync(offsetY, fromScale, blur, itemDelay));
            index++;
        }
        return Task.WhenAll(tasks);
    }



    /// <summary>
    /// 用 spring 缩放到目标值，按压回馈用
    /// </summary>
    public static void SpringScaleTo(this UIElement element, float scale)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        EnsureCenterPoint(element);
        if (!MotionSettings.IsEnabled)
        {
            visual.StopAnimation(nameof(Visual.Scale));
            visual.Scale = Vector3.One;
            return;
        }
        Compositor compositor = visual.Compositor;
        SpringVector3NaturalMotionAnimation spring = compositor.CreateSpringVector3Animation();
        spring.FinalValue = new Vector3(scale, scale, 1);
        spring.DampingRatio = MotionTokens.SpringDampingRatio;
        spring.Period = MotionTokens.SpringPeriod;
        visual.StartAnimation(nameof(Visual.Scale), spring);
    }



    /// <summary>
    /// 立即隐藏 Visual，用在马上要进场的元素上，避免先闪一帧
    /// </summary>
    public static void HideForEnter(this UIElement element)
    {
        if (MotionSettings.IsEnabled)
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.Opacity = 0;
        }
    }



    /// <summary>
    /// 停止所有动画并回到原状
    /// </summary>
    public static void ResetMotion(this UIElement element)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.StopAnimation(nameof(Visual.Scale));
        visual.Opacity = 1;
        visual.Scale = Vector3.One;
        visual.StopAnimation("Translation");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        BlurLayer.Remove(element);
    }



    /// <summary>
    /// 等 ScopedBatch 结束
    /// </summary>
    public static Task AsTask(this CompositionScopedBatch batch)
    {
        var tcs = new TaskCompletionSource();
        batch.Completed += (_, _) => tcs.TrySetResult();
        return tcs.Task;
    }



    private static readonly ConditionalWeakTable<UIElement, object> _centerPointTracked = new();


    /// <summary>
    /// 缩放以元素中心为原点，尺寸变化时跟着更新
    /// </summary>
    private static void EnsureCenterPoint(UIElement element)
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        Vector2 size = element.ActualSize;
        visual.CenterPoint = new Vector3(size.X / 2, size.Y / 2, 0);
        if (element is FrameworkElement fe && !_centerPointTracked.TryGetValue(element, out _))
        {
            _centerPointTracked.Add(element, new object());
            fe.SizeChanged += (_, e) =>
            {
                ElementCompositionPreview.GetElementVisual(fe).CenterPoint = new Vector3((float)e.NewSize.Width / 2, (float)e.NewSize.Height / 2, 0);
            };
        }
    }



    /// <summary>
    /// 盖在元素内容上的背景模糊层，用来做「由糊变清」。
    /// 它只在动画期间存在，结束后就移除，不常驻消耗 GPU，也不会让文字变糊。
    /// </summary>
    private static class BlurLayer
    {

        private static readonly ConditionalWeakTable<UIElement, SpriteVisual> _layers = new();


        public static void Animate(UIElement element, float from, float to, TimeSpan duration, TimeSpan delay)
        {
            Remove(element);
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            Compositor compositor = visual.Compositor;
            CompositionEffectBrush brush = BackdropBlurBrush.CreateBlurBrush(compositor, from, Color.FromArgb(0, 0, 0, 0));
            SpriteVisual sprite = compositor.CreateSpriteVisual();
            sprite.Brush = brush;
            sprite.RelativeSizeAdjustment = Vector2.One;
            ElementCompositionPreview.SetElementChildVisual(element, sprite);
            _layers.AddOrUpdate(element, sprite);

            CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            ScalarKeyFrameAnimation blur = compositor.CreateScalarKeyFrameAnimation();
            blur.InsertKeyFrame(0, from);
            blur.InsertKeyFrame(1, to, MotionTokens.Decelerate(compositor));
            blur.Duration = duration;
            blur.DelayTime = delay;
            blur.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            brush.StartAnimation("Blur.BlurAmount", blur);
            batch.End();
            batch.Completed += (_, _) =>
            {
                if (_layers.TryGetValue(element, out SpriteVisual? current) && current == sprite)
                {
                    Remove(element);
                }
            };
        }


        public static void Remove(UIElement element)
        {
            if (_layers.TryGetValue(element, out SpriteVisual? sprite))
            {
                _layers.Remove(element);
                ElementCompositionPreview.SetElementChildVisual(element, null);
                sprite.Brush?.Dispose();
                sprite.Dispose();
            }
        }

    }


}
