using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
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
/// 动画只是锦上添花，任何一步出错都只记日志并让元素回到原状，不能让应用崩溃。
/// </summary>
public static class Motion
{


    private static readonly ILogger _logger = AppConfig.GetLogger<MotionLog>();

    private sealed class MotionLog { }



    /// <summary>
    /// 进场：淡入、从下方滑入、可选从略小放大到原尺寸、可选由糊变清
    /// </summary>
    /// <param name="offsetY">起始的 Y 偏移，正数表示从下方进入</param>
    /// <param name="fromScale">起始缩放，1 表示不缩放</param>
    /// <param name="blur">起始模糊量，0 表示不模糊</param>
    public static Task EnterAsync(this UIElement element, float offsetY = 12, float fromScale = 1, float blur = 0, TimeSpan delay = default, TimeSpan? duration = null)
    {
        try
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            if (!MotionSettings.IsEnabled)
            {
                ResetMotion(element);
                return Task.CompletedTask;
            }
            Compositor compositor = visual.Compositor;
            TimeSpan time = duration ?? MotionTokens.Slow;
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
                EnableTranslation(element);
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
        catch (Exception ex)
        {
            return Fail(ex, element, nameof(EnterAsync), visible: true);
        }
    }



    /// <summary>
    /// 退场：淡出，可选位移、缩放、模糊，结束后元素保持透明，由调用方决定是否折叠
    /// </summary>
    public static Task ExitAsync(this UIElement element, float offsetY = 0, float toScale = 1, float blur = 0, TimeSpan? duration = null)
    {
        try
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
            CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

            ScalarKeyFrameAnimation opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(1, 0, MotionTokens.Accelerate(compositor));
            opacity.Duration = time;
            visual.StartAnimation(nameof(Visual.Opacity), opacity);

            if (offsetY != 0)
            {
                EnableTranslation(element);
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
        catch (Exception ex)
        {
            return Fail(ex, element, nameof(ExitAsync), visible: false);
        }
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
    /// 导航并用统一的进场代替 Frame 内置的转场：淡入、上滑、由糊变清
    /// </summary>
    public static bool NavigateWithMotion(this Frame frame, Type page, object? parameter = null, float offsetY = 12, float fromScale = 1, float blur = MotionTokens.BlurSubtle)
    {
        bool result = frame.Navigate(page, parameter, new SuppressNavigationTransitionInfo());
        _ = frame.EnterAsync(offsetY, fromScale, blur);
        return result;
    }



    /// <summary>
    /// 从左边长出来，进度条用
    /// </summary>
    public static Task GrowFromLeftAsync(this UIElement element, TimeSpan delay = default)
    {
        try
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            if (!MotionSettings.IsEnabled)
            {
                visual.StopAnimation(nameof(Visual.Scale));
                visual.Scale = Vector3.One;
                return Task.CompletedTask;
            }
            Compositor compositor = visual.Compositor;
            visual.CenterPoint = new Vector3(0, element.ActualSize.Y / 2, 0);
            CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            Vector3KeyFrameAnimation scale = compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(0, new Vector3(0, 1, 1));
            scale.InsertKeyFrame(1, Vector3.One, MotionTokens.Decelerate(compositor));
            scale.Duration = MotionTokens.Emphasized;
            scale.DelayTime = delay;
            scale.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            visual.StartAnimation(nameof(Visual.Scale), scale);
            batch.End();
            return batch.AsTask();
        }
        catch (Exception ex)
        {
            return Fail(ex, element, nameof(GrowFromLeftAsync), visible: true);
        }
    }



    /// <summary>
    /// 用 spring 缩放到目标值，按压回馈用
    /// </summary>
    public static void SpringScaleTo(this UIElement element, float scale)
    {
        try
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
        catch (Exception ex)
        {
            _ = Fail(ex, element, nameof(SpringScaleTo), visible: true);
        }
    }



    /// <summary>
    /// 停止所有动画并回到原状
    /// </summary>
    public static void ResetMotion(this UIElement element)
    {
        try
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.StopAnimation(nameof(Visual.Scale));
            visual.Opacity = 1;
            visual.Scale = Vector3.One;
            // 没启用 Translation 的 Visual 上停 Translation 动画会抛 ArgumentException
            if (_translationEnabled.TryGetValue(element, out _))
            {
                visual.StopAnimation("Translation");
                visual.Properties.InsertVector3("Translation", Vector3.Zero);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reset motion of {element}", element.GetType().Name);
        }
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



    /// <summary>
    /// 动画出错时记日志，并把元素放回可见或透明的终点状态
    /// </summary>
    private static Task Fail(Exception ex, UIElement element, string operation, bool visible)
    {
        _logger.LogWarning(ex, "{operation} of {element}", operation, element.GetType().Name);
        try
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.Opacity = visible ? 1 : 0;
        }
        catch { }
        BlurLayer.Remove(element);
        return Task.CompletedTask;
    }



    private static readonly ConditionalWeakTable<UIElement, object> _translationEnabled = new();


    private static void EnableTranslation(UIElement element)
    {
        if (!_translationEnabled.TryGetValue(element, out _))
        {
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            _translationEnabled.Add(element, new object());
        }
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
    /// 同时存在的模糊层有上限，超过时只淡入不模糊，避免一次换页叠好几层整块的高斯模糊。
    /// </summary>
    private static class BlurLayer
    {

        private const int MaxActiveLayers = 2;

        private static readonly ConditionalWeakTable<UIElement, SpriteVisual> _layers = new();

        private static int _activeCount;


        public static void Animate(UIElement element, float from, float to, TimeSpan duration, TimeSpan delay)
        {
            Remove(element);
            if (_activeCount >= MaxActiveLayers)
            {
                return;
            }
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            Compositor compositor = visual.Compositor;
            CompositionEffectBrush brush = BackdropBlurBrush.CreateBlurBrush(compositor, from, Color.FromArgb(0, 0, 0, 0));
            SpriteVisual sprite = compositor.CreateSpriteVisual();
            sprite.Brush = brush;
            sprite.RelativeSizeAdjustment = Vector2.One;
            ElementCompositionPreview.SetElementChildVisual(element, sprite);
            _layers.AddOrUpdate(element, sprite);
            _activeCount++;

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
                _activeCount = Math.Max(0, _activeCount - 1);
                try
                {
                    ElementCompositionPreview.SetElementChildVisual(element, null);
                    sprite.Brush?.Dispose();
                    sprite.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Remove blur layer of {element}", element.GetType().Name);
                }
            }
        }

    }


}
