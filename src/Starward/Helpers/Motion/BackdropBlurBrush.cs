using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;
using Windows.UI;

namespace Starward.Helpers;

/// <summary>
/// 模糊量和染色都能做动画的背景模糊笔刷。
/// AcrylicBrush 的模糊量是固定的，做不出「背景慢慢退到雾面后面」的过渡，所以自己用 Composition 拼一个。
/// 系统关闭透明效果时改用 <see cref="XamlCompositionBrushBase.FallbackColor"/>。
/// </summary>
public sealed partial class BackdropBlurBrush : XamlCompositionBrushBase
{

    private const string BlurAmountPath = "Blur.BlurAmount";

    private const string TintColorPath = "Tint.Color";


    private CompositionEffectBrush? _effectBrush;

    private PendingAnimation? _pending;

    private bool _suppressPropertyChanged;


    public double BlurAmount
    {
        get => (double)GetValue(BlurAmountProperty);
        set => SetValue(BlurAmountProperty, value);
    }
    public static readonly DependencyProperty BlurAmountProperty =
        DependencyProperty.Register(nameof(BlurAmount), typeof(double), typeof(BackdropBlurBrush), new PropertyMetadata(0d, OnEffectPropertyChanged));


    public Color TintColor
    {
        get => (Color)GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }
    public static readonly DependencyProperty TintColorProperty =
        DependencyProperty.Register(nameof(TintColor), typeof(Color), typeof(BackdropBlurBrush), new PropertyMetadata(Color.FromArgb(0, 0, 0, 0), OnEffectPropertyChanged));


    /// <summary>
    /// 染色的不透明度，会乘上 <see cref="TintColor"/> 自带的 alpha
    /// </summary>
    public double TintOpacity
    {
        get => (double)GetValue(TintOpacityProperty);
        set => SetValue(TintOpacityProperty, value);
    }
    public static readonly DependencyProperty TintOpacityProperty =
        DependencyProperty.Register(nameof(TintOpacity), typeof(double), typeof(BackdropBlurBrush), new PropertyMetadata(0d, OnEffectPropertyChanged));



    /// <summary>
    /// 这块模糊会盖住整个背景，显示期间背景视频可以停在当前帧
    /// </summary>
    public bool CoversBackground { get; set; }


    private static int _coveringCount;

    private bool _covering;


    /// <summary>
    /// 有没有盖住整个背景的模糊正在显示，参数为 true 表示有
    /// </summary>
    public static event Action<bool>? BackgroundCoveredChanged;


    private void SetCovering(bool covering)
    {
        if (!CoversBackground || _covering == covering)
        {
            return;
        }
        _covering = covering;
        int before = _coveringCount;
        _coveringCount = Math.Max(0, _coveringCount + (covering ? 1 : -1));
        if ((before > 0) != (_coveringCount > 0))
        {
            BackgroundCoveredChanged?.Invoke(_coveringCount > 0);
        }
    }



    private static void OnEffectPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BackdropBlurBrush brush && !brush._suppressPropertyChanged && brush._effectBrush is not null)
        {
            brush._effectBrush.Properties.InsertScalar(BlurAmountPath, (float)brush.BlurAmount);
            brush._effectBrush.Properties.InsertColor(TintColorPath, brush.GetTint(brush.TintOpacity));
        }
    }



    protected override void OnConnected()
    {
        if (CompositionBrush is not null)
        {
            return;
        }
        Compositor compositor = CompositionTarget.GetCompositorForCurrentThread();
        SetCovering(true);
        if (!MotionSettings.IsTransparencyEnabled)
        {
            CompositionBrush = compositor.CreateColorBrush(FallbackColor);
            _pending = null;
            return;
        }
        _effectBrush = CreateBlurBrush(compositor, (float)BlurAmount, GetTint(TintOpacity));
        CompositionBrush = _effectBrush;
        if (_pending is PendingAnimation pending)
        {
            // 刚变为可见时笔刷还没连上，动画要等连上之后再从起点播
            _pending = null;
            _effectBrush.Properties.InsertScalar(BlurAmountPath, pending.FromBlur);
            _effectBrush.Properties.InsertColor(TintColorPath, GetTint(pending.FromTintOpacity));
            StartAnimations(pending.Duration, pending.Delay);
        }
    }


    protected override void OnDisconnected()
    {
        SetCovering(false);
        CompositionBrush?.Dispose();
        CompositionBrush = null;
        _effectBrush = null;
    }



    /// <summary>
    /// 把模糊量和染色过渡到目标值，动画关闭时直接设置
    /// </summary>
    public Task AnimateToAsync(double blurAmount, double tintOpacity, TimeSpan duration, TimeSpan delay = default)
    {
        float fromBlur = (float)BlurAmount;
        float fromTint = (float)TintOpacity;
        _suppressPropertyChanged = true;
        BlurAmount = blurAmount;
        TintOpacity = tintOpacity;
        _suppressPropertyChanged = false;

        if (!MotionSettings.IsEnabled || duration <= TimeSpan.Zero)
        {
            _pending = null;
            if (_effectBrush is not null)
            {
                _effectBrush.StopAnimation(BlurAmountPath);
                _effectBrush.StopAnimation(TintColorPath);
                _effectBrush.Properties.InsertScalar(BlurAmountPath, (float)blurAmount);
                _effectBrush.Properties.InsertColor(TintColorPath, GetTint(tintOpacity));
            }
            return Task.CompletedTask;
        }
        if (_effectBrush is null)
        {
            _pending = new PendingAnimation(fromBlur, fromTint, duration, delay);
            return Task.Delay(delay + duration);
        }
        try
        {
            return StartAnimations(duration, delay);
        }
        catch
        {
            // 动画起不来就直接到终点，不能因为过渡效果让应用崩溃
            _effectBrush.Properties.InsertScalar(BlurAmountPath, (float)blurAmount);
            _effectBrush.Properties.InsertColor(TintColorPath, GetTint(tintOpacity));
            return Task.CompletedTask;
        }
    }



    private Task StartAnimations(TimeSpan duration, TimeSpan delay)
    {
        CompositionEffectBrush brush = _effectBrush!;
        Compositor compositor = brush.Compositor;
        CompositionScopedBatch batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        ScalarKeyFrameAnimation blur = compositor.CreateScalarKeyFrameAnimation();
        blur.InsertKeyFrame(1, (float)BlurAmount, MotionTokens.Standard(compositor));
        blur.Duration = duration;
        blur.DelayTime = delay;
        brush.StartAnimation(BlurAmountPath, blur);

        ColorKeyFrameAnimation tint = compositor.CreateColorKeyFrameAnimation();
        tint.InsertKeyFrame(1, GetTint(TintOpacity), MotionTokens.Standard(compositor));
        tint.Duration = duration;
        tint.DelayTime = delay;
        brush.StartAnimation(TintColorPath, tint);

        batch.End();
        return batch.AsTask();
    }



    private Color GetTint(double opacity)
    {
        Color color = TintColor;
        return Color.FromArgb((byte)Math.Clamp(color.A * opacity, 0, 255), color.R, color.G, color.B);
    }



    /// <summary>
    /// 背景模糊加一层染色，模糊量和染色都可以做动画
    /// </summary>
    internal static CompositionEffectBrush CreateBlurBrush(Compositor compositor, float blurAmount, Color tint)
    {
        var blur = new GaussianBlurEffect
        {
            Name = "Blur",
            BlurAmount = blurAmount,
            BorderMode = EffectBorderMode.Hard,
            // 整窗的模糊用 Speed：内部先缩小再模糊，模糊量大时画质差不多，GPU 开销少很多
            Optimization = EffectOptimization.Speed,
            Source = new CompositionEffectSourceParameter("Backdrop"),
        };
        var color = new ColorSourceEffect
        {
            Name = "Tint",
            Color = tint,
        };
        var composite = new CompositeEffect
        {
            Mode = CanvasComposite.SourceOver,
            Sources = { blur, color },
        };
        CompositionEffectFactory factory = compositor.CreateEffectFactory(composite, [BlurAmountPath, TintColorPath]);
        CompositionEffectBrush brush = factory.CreateBrush();
        brush.SetSourceParameter("Backdrop", compositor.CreateBackdropBrush());
        return brush;
    }



    private readonly record struct PendingAnimation(float FromBlur, float FromTintOpacity, TimeSpan Duration, TimeSpan Delay);


}
