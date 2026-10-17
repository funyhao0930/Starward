using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;
using Windows.UI;

namespace Starward.Helpers;

/// <summary>
/// 进度环变成打勾：在进度环同一位置画一个同样大小、同样颜色的圆，
/// 圆圈顺时针收线消失的同时一笔画出勾，最后轻轻弹一下，看起来像圆圈变形成勾。
/// </summary>
public static class CheckMorph
{

    private static readonly ILogger _logger = AppConfig.GetLogger<CheckMorphLog>();

    private sealed class CheckMorphLog { }



    /// <summary>
    /// 在 <paramref name="host"/> 上播放圆圈变勾。host 应和进度环同样大小、叠在同一位置
    /// </summary>
    /// <param name="ringThickness">圆圈线宽，和进度环一致才看不出接缝</param>
    public static void Play(FrameworkElement host, Color ringColor, Color checkColor, float ringThickness = 3)
    {
        try
        {
            Compositor compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
            float size = (float)Math.Max(host.Width, host.ActualWidth);
            if (size <= 0)
            {
                size = 28;
            }
            float center = size / 2;
            float radius = center - ringThickness / 2;

            ShapeVisual shapeVisual = compositor.CreateShapeVisual();
            shapeVisual.Size = new Vector2(size, size);

            // 圆圈：从 12 点钟方向开始，和进度环的起点一致
            CompositionEllipseGeometry ellipse = compositor.CreateEllipseGeometry();
            ellipse.Center = new Vector2(center, center);
            ellipse.Radius = new Vector2(radius, radius);
            CompositionSpriteShape ring = compositor.CreateSpriteShape(ellipse);
            ring.StrokeBrush = compositor.CreateColorBrush(ringColor);
            ring.StrokeThickness = ringThickness;
            ring.StrokeStartCap = CompositionStrokeCap.Round;
            ring.StrokeEndCap = CompositionStrokeCap.Round;
            ring.CenterPoint = new Vector2(center, center);
            ring.RotationAngleInDegrees = -90;
            shapeVisual.Shapes.Add(ring);

            // 勾：按 28 的格子设计，再按实际大小缩放
            float s = size / 28f;
            using var builder = new CanvasPathBuilder(null);
            builder.BeginFigure(8.2f * s, 14.6f * s);
            builder.AddLine(12.4f * s, 18.6f * s);
            builder.AddLine(19.8f * s, 10.2f * s);
            builder.EndFigure(CanvasFigureLoop.Open);
            CompositionPathGeometry checkGeometry = compositor.CreatePathGeometry(new CompositionPath(CanvasGeometry.CreatePath(builder)));
            CompositionSpriteShape check = compositor.CreateSpriteShape(checkGeometry);
            check.StrokeBrush = compositor.CreateColorBrush(checkColor);
            check.StrokeThickness = 2.4f * s;
            check.StrokeStartCap = CompositionStrokeCap.Round;
            check.StrokeEndCap = CompositionStrokeCap.Round;
            check.StrokeLineJoin = CompositionStrokeLineJoin.Round;
            shapeVisual.Shapes.Add(check);

            ElementCompositionPreview.SetElementChildVisual(host, shapeVisual);

            if (!MotionSettings.IsEnabled)
            {
                ellipse.TrimStart = 1;
                return;
            }

            CompositionEasingFunction ease = MotionTokens.Standard(compositor);

            // 圆圈从起点开始收线，像被抽走一样消失
            ScalarKeyFrameAnimation unwind = compositor.CreateScalarKeyFrameAnimation();
            unwind.InsertKeyFrame(0, 0);
            unwind.InsertKeyFrame(1, 1, ease);
            unwind.Duration = TimeSpan.FromMilliseconds(420);
            ellipse.StartAnimation(nameof(CompositionGeometry.TrimStart), unwind);

            // 圆圈收到一半时开始画勾
            checkGeometry.TrimEnd = 0;
            ScalarKeyFrameAnimation draw = compositor.CreateScalarKeyFrameAnimation();
            draw.InsertKeyFrame(0, 0);
            draw.InsertKeyFrame(1, 1, MotionTokens.Decelerate(compositor));
            draw.Duration = TimeSpan.FromMilliseconds(360);
            draw.DelayTime = TimeSpan.FromMilliseconds(220);
            draw.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            checkGeometry.StartAnimation(nameof(CompositionGeometry.TrimEnd), draw);

            // 收线时整体略缩，画完勾弹回原大小
            shapeVisual.CenterPoint = new Vector3(center, center, 0);
            Vector3KeyFrameAnimation pop = compositor.CreateVector3KeyFrameAnimation();
            pop.InsertKeyFrame(0, Vector3.One);
            pop.InsertKeyFrame(0.45f, new Vector3(0.82f, 0.82f, 1), ease);
            pop.InsertKeyFrame(0.8f, new Vector3(1.08f, 1.08f, 1), MotionTokens.Decelerate(compositor));
            pop.InsertKeyFrame(1, Vector3.One, ease);
            pop.Duration = TimeSpan.FromMilliseconds(700);
            shapeVisual.StartAnimation(nameof(Visual.Scale), pop);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Play check morph");
        }
    }



    public static void Reset(FrameworkElement host)
    {
        try
        {
            ElementCompositionPreview.SetElementChildVisual(host, null);
        }
        catch { }
    }


}
