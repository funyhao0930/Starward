using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Controls;


/// <summary>
/// PulsoIcon（https://github.com/CityPulso/PulsoIcon）的动画线条图标。
/// 每个形状有自己的动作，指针移到所在的按钮或导航项上时播一次。
/// 用 Composition 形状画。继承 FontIcon（IconElement 本身在 C# 不能继承）但不显示字形，
/// 所以可以放进 NavigationViewItem.Icon 之类只收 IconElement 的地方，FontSize 就是图标大小。
/// 图标数据由 tools/pulsoicon/generate.mjs 生成。
/// </summary>
public sealed partial class PulsoIcon : FontIcon
{

    private static readonly ILogger _logger = AppConfig.GetLogger<PulsoIconLog>();

    private sealed class PulsoIconLog { }


    /// <summary>
    /// 动作可能把形状移出 24×24 的格子，画布四周各留出一半的空间
    /// </summary>
    private const float Padding = 0.5f;


    private Compositor? _compositor;

    private ShapeVisual? _shapeVisual;

    private CompositionContainerShape? _root;

    private readonly List<NodeVisual> _nodes = [];

    private NodeVisual? _svg;

    private UIElement? _trigger;

    private bool _playing;

    /// <summary>
    /// 上次开始播放的时间。视窗最小化时动画批次可能一直等不到完成事件，超过最长动作时间就当作播完了
    /// </summary>
    private long _playStartedTick;

    private long _foregroundToken;

    private SolidColorBrush? _watchedBrush;

    private long _brushColorToken;


    private sealed record NodeVisual(CompositionShape Shape, CompositionGeometry? Geometry, CompositionColorBrush? Brush, PulsoMotion? Motion);



    public PulsoIcon()
    {
        Glyph = string.Empty;
        Loaded += PulsoIcon_Loaded;
        Unloaded += PulsoIcon_Unloaded;
        SizeChanged += (_, _) => UpdateLayoutScale();
        ActualThemeChanged += (_, _) => ApplyColor();
    }



    public PulsoIconKind Kind
    {
        get => (PulsoIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(PulsoIconKind), typeof(PulsoIcon), new PropertyMetadata(PulsoIconKind.None, (d, _) => ((PulsoIcon)d).Rebuild()));


    /// <summary>
    /// 线宽，单位是 24×24 格子里的单位。PulsoIcon 原版是 2，
    /// 这里默认 1.5：16 像素时约 1 像素，和 Segoe Fluent Icons 的线一样细，混在一起不突兀
    /// </summary>
    public double StrokeWidth
    {
        get => (double)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public static readonly DependencyProperty StrokeWidthProperty =
        DependencyProperty.Register(nameof(StrokeWidth), typeof(double), typeof(PulsoIcon), new PropertyMetadata(1.5d, (d, _) => ((PulsoIcon)d).ApplyStrokeWidth()));



    #region 布局


    protected override Size MeasureOverride(Size availableSize)
    {
        // 和字体图标一样，FontSize 就是边长；设了 Width / Height 时由框架按那个尺寸排
        return new Size(FontSize, FontSize);
    }


    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }


    private void UpdateLayoutScale()
    {
        if (_shapeVisual is null || _root is null)
        {
            return;
        }
        Vector2 size = ActualSize;
        float side = MathF.Min(size.X, size.Y);
        float scale = side / 24;
        Vector2 pad = new(side * Padding);
        _shapeVisual.Size = size + pad * 2;
        _shapeVisual.Offset = new Vector3(-pad, 0);
        _root.Scale = new Vector2(scale);
        _root.Offset = pad + (size - new Vector2(side)) / 2;
    }


    #endregion



    #region 绘制


    private void Rebuild()
    {
        try
        {
            _nodes.Clear();
            _svg = null;
            _playing = false;
            if (Kind is PulsoIconKind.None || !PulsoIconData.Icons.TryGetValue(Kind, out PulsoIconDefinition? icon))
            {
                ElementCompositionPreview.SetElementChildVisual(this, null);
                _shapeVisual = null;
                _root = null;
                return;
            }

            _compositor ??= ElementCompositionPreview.GetElementVisual(this).Compositor;
            _shapeVisual = _compositor.CreateShapeVisual();
            _root = _compositor.CreateContainerShape();
            _shapeVisual.Shapes.Add(_root);

            // 整个图标的动作放在一层容器上，形状的动作放在各自的形状上
            CompositionContainerShape group = _compositor.CreateContainerShape();
            _root.Shapes.Add(group);
            if (icon.Svg is not null)
            {
                group.CenterPoint = new Vector2(icon.Svg.OX, icon.Svg.OY);
                _svg = new NodeVisual(group, null, null, icon.Svg);
            }

            foreach (PulsoNode node in icon.Nodes)
            {
                CompositionPathGeometry geometry = _compositor.CreatePathGeometry(new CompositionPath(BuildGeometry(node.Ops)));
                CompositionSpriteShape shape = _compositor.CreateSpriteShape(geometry);
                CompositionColorBrush brush = _compositor.CreateColorBrush();
                shape.StrokeBrush = brush;
                if (node.Filled)
                {
                    shape.FillBrush = brush;
                }
                shape.StrokeStartCap = CompositionStrokeCap.Round;
                shape.StrokeEndCap = CompositionStrokeCap.Round;
                shape.StrokeLineJoin = CompositionStrokeLineJoin.Round;
                if (node.Motion is not null)
                {
                    shape.CenterPoint = new Vector2(node.Motion.OX, node.Motion.OY);
                }
                group.Shapes.Add(shape);
                _nodes.Add(new NodeVisual(shape, geometry, brush, node.Motion));
            }

            ElementCompositionPreview.SetElementChildVisual(this, _shapeVisual);
            ApplyStrokeWidth();
            ApplyColor();
            UpdateLayoutScale();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Build PulsoIcon {Kind}", Kind);
        }
    }


    private static CanvasGeometry BuildGeometry(float[] ops)
    {
        using var builder = new CanvasPathBuilder(null);
        bool open = false;
        Vector2 start = default;
        Vector2 Point(int i) => new(ops[i], ops[i + 1]);
        void EnsureFigure()
        {
            // SVG 允许 Z 之后不写 M 直接画，起点是上一段的起点
            if (!open)
            {
                builder.BeginFigure(start);
                open = true;
            }
        }
        int i = 0;
        while (i < ops.Length)
        {
            switch ((int)ops[i++])
            {
                case 0:
                    if (open)
                    {
                        builder.EndFigure(CanvasFigureLoop.Open);
                    }
                    start = Point(i);
                    builder.BeginFigure(start);
                    open = true;
                    i += 2;
                    break;
                case 1:
                    EnsureFigure();
                    builder.AddLine(Point(i));
                    i += 2;
                    break;
                case 2:
                    EnsureFigure();
                    builder.AddCubicBezier(Point(i), Point(i + 2), Point(i + 4));
                    i += 6;
                    break;
                default:
                    if (open)
                    {
                        builder.EndFigure(CanvasFigureLoop.Closed);
                        open = false;
                    }
                    break;
            }
        }
        if (open)
        {
            builder.EndFigure(CanvasFigureLoop.Open);
        }
        return CanvasGeometry.CreatePath(builder);
    }


    private void ApplyStrokeWidth()
    {
        foreach (NodeVisual node in _nodes)
        {
            if (node.Shape is CompositionSpriteShape sprite)
            {
                sprite.StrokeThickness = (float)StrokeWidth;
            }
        }
    }


    private Color CurrentColor()
    {
        if (Foreground is SolidColorBrush brush)
        {
            Color c = brush.Color;
            return Color.FromArgb((byte)(c.A * Math.Clamp(brush.Opacity, 0, 1)), c.R, c.G, c.B);
        }
        return ActualTheme is ElementTheme.Light ? Colors.Black : Colors.White;
    }


    private void ApplyColor()
    {
        WatchBrush();
        if (_playing)
        {
            // 播放中透明度动画占着颜色，播完再上色
            return;
        }
        Color color = CurrentColor();
        foreach (NodeVisual node in _nodes)
        {
            if (node.Brush is not null)
            {
                node.Brush.StopAnimation(nameof(CompositionColorBrush.Color));
                node.Brush.Color = color;
            }
        }
    }


    /// <summary>
    /// 模板的视觉状态常常直接改画刷的颜色，而不是换一个画刷，所以画刷本身也要盯着
    /// </summary>
    private void WatchBrush()
    {
        SolidColorBrush? brush = Foreground as SolidColorBrush;
        if (ReferenceEquals(brush, _watchedBrush))
        {
            return;
        }
        _watchedBrush?.UnregisterPropertyChangedCallback(SolidColorBrush.ColorProperty, _brushColorToken);
        _watchedBrush = brush;
        if (brush is not null)
        {
            _brushColorToken = brush.RegisterPropertyChangedCallback(SolidColorBrush.ColorProperty, (_, _) => ApplyColor());
        }
    }


    #endregion



    #region 触发


    private void PulsoIcon_Loaded(object sender, RoutedEventArgs e)
    {
        _foregroundToken = RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => ApplyColor());
        if (_shapeVisual is null)
        {
            Rebuild();
        }
        else
        {
            ApplyColor();
        }
        AttachTrigger();
    }


    private void PulsoIcon_Unloaded(object sender, RoutedEventArgs e)
    {
        UnregisterPropertyChangedCallback(ForegroundProperty, _foregroundToken);
        _watchedBrush?.UnregisterPropertyChangedCallback(SolidColorBrush.ColorProperty, _brushColorToken);
        _watchedBrush = null;
        DetachTrigger();
    }


    private void AttachTrigger()
    {
        DetachTrigger();
        _trigger = FindTrigger();
        _trigger.AddHandler(PointerEnteredEvent, new PointerEventHandler(Trigger_PointerEntered), true);
        _trigger.GotFocus += Trigger_GotFocus;
    }


    private void DetachTrigger()
    {
        if (_trigger is not null)
        {
            _trigger.RemoveHandler(PointerEnteredEvent, new PointerEventHandler(Trigger_PointerEntered));
            _trigger.GotFocus -= Trigger_GotFocus;
            _trigger = null;
        }
    }


    /// <summary>
    /// 往上找最近的按钮、导航项或列表项，找不到就用自己
    /// </summary>
    private UIElement FindTrigger()
    {
        DependencyObject? current = VisualTreeHelper.GetParent(this);
        for (int depth = 0; current is not null && depth < 16; depth++)
        {
            if (current is ButtonBase or NavigationViewItem or SelectorItem or MenuFlyoutItemBase)
            {
                return (UIElement)current;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return this;
    }


    private void Trigger_PointerEntered(object sender, PointerRoutedEventArgs e) => Play();


    private void Trigger_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_trigger is Control { FocusState: FocusState.Keyboard })
        {
            Play();
        }
    }


    #endregion



    #region 动画


    /// <summary>
    /// 播一次动作。播放中再触发会被忽略（超过 1.5 秒还没收到完成事件就不再等），每个动作结束时都回到原样，所以中途离开也让它播完。
    /// </summary>
    public void Play()
    {
        if (_playing && Environment.TickCount64 - _playStartedTick < 1500)
        {
            return;
        }
        if (_compositor is null || _nodes.Count == 0 || !MotionSettings.IsEnabled)
        {
            return;
        }
        try
        {
            _playing = true;
            _playStartedTick = Environment.TickCount64;
            Color color = CurrentColor();
            CompositionScopedBatch batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            if (_svg is not null)
            {
                Animate(_svg, color);
            }
            foreach (NodeVisual node in _nodes)
            {
                Animate(node, color);
            }
            batch.End();
            batch.Completed += (_, _) =>
            {
                _playing = false;
                ApplyColor();
            };
        }
        catch (Exception ex)
        {
            _playing = false;
            _logger.LogWarning(ex, "Play PulsoIcon {Kind}", Kind);
        }
    }


    private void Animate(NodeVisual node, Color color)
    {
        if (node.Motion is null || _compositor is null)
        {
            return;
        }
        foreach (PulsoTrack track in PulsoRecipes.Build(node.Motion))
        {
            switch (track.Channel)
            {
                case PulsoChannel.X:
                    node.Shape.StartAnimation("Offset.X", Scalar(track));
                    break;
                case PulsoChannel.Y:
                    node.Shape.StartAnimation("Offset.Y", Scalar(track));
                    break;
                case PulsoChannel.Rotate:
                    node.Shape.StartAnimation(nameof(CompositionShape.RotationAngleInDegrees), Scalar(track));
                    break;
                case PulsoChannel.Scale:
                    node.Shape.StartAnimation("Scale.X", Scalar(track));
                    node.Shape.StartAnimation("Scale.Y", Scalar(track));
                    break;
                case PulsoChannel.ScaleX:
                    node.Shape.StartAnimation("Scale.X", Scalar(track));
                    break;
                case PulsoChannel.ScaleY:
                    node.Shape.StartAnimation("Scale.Y", Scalar(track));
                    break;
                case PulsoChannel.PathLength:
                    node.Geometry?.StartAnimation(nameof(CompositionGeometry.TrimEnd), Scalar(track));
                    break;
                case PulsoChannel.PathOffset:
                    node.Geometry?.StartAnimation(nameof(CompositionGeometry.TrimOffset), Scalar(track));
                    break;
                case PulsoChannel.Opacity:
                    // Composition 形状没有透明度，改画刷颜色的 alpha
                    node.Brush?.StartAnimation(nameof(CompositionColorBrush.Color), Opacity(track, color));
                    break;
            }
        }
    }


    private ScalarKeyFrameAnimation Scalar(PulsoTrack track)
    {
        ScalarKeyFrameAnimation animation = _compositor!.CreateScalarKeyFrameAnimation();
        CompositionEasingFunction ease = Ease(track.Ease);
        for (int i = 0; i < track.Values.Length; i++)
        {
            animation.InsertKeyFrame(Time(track, i), track.Values[i], ease);
        }
        Timing(animation, track);
        return animation;
    }


    private ColorKeyFrameAnimation Opacity(PulsoTrack track, Color color)
    {
        ColorKeyFrameAnimation animation = _compositor!.CreateColorKeyFrameAnimation();
        CompositionEasingFunction ease = Ease(track.Ease);
        for (int i = 0; i < track.Values.Length; i++)
        {
            byte a = (byte)Math.Clamp(color.A * track.Values[i], 0, 255);
            animation.InsertKeyFrame(Time(track, i), Color.FromArgb(a, color.R, color.G, color.B), ease);
        }
        Timing(animation, track);
        return animation;
    }


    private static float Time(PulsoTrack track, int i)
    {
        if (track.Times is { } times && times.Length == track.Values.Length)
        {
            return times[i];
        }
        return track.Values.Length == 1 ? 1 : (float)i / (track.Values.Length - 1);
    }


    private static void Timing(KeyFrameAnimation animation, PulsoTrack track)
    {
        animation.Duration = TimeSpan.FromSeconds(Math.Max(track.Duration, 0.01));
        animation.DelayTime = TimeSpan.FromSeconds(track.Delay);
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
    }


    private CompositionEasingFunction Ease(Vector4 e)
    {
        return _compositor!.CreateCubicBezierEasingFunction(new Vector2(e.X, e.Y), new Vector2(e.Z, e.W));
    }


    #endregion


}
