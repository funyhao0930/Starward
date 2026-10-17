using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Starward.Helpers;

/// <summary>
/// 类似 macOS 程序坞的放大效果：指针附近的元素按距离放大，旁边的元素被推开让位。
/// 只改元素 Composition Visual 的 Scale 和 Translation，不触发布局，命中区域也保持原样。
/// 指针移入移出时效果强度平滑过渡，静止后停止逐帧计算。
/// </summary>
public sealed class DockMagnifier
{

    private static readonly ILogger _logger = AppConfig.GetLogger<DockMagnifier>();


    /// <summary>
    /// 放大时元素固定的那一边
    /// </summary>
    public enum Anchor
    {
        /// <summary>贴着上边往下长（顶部的图标栏）</summary>
        Top,
        /// <summary>贴着下边往上长（底部的按钮）</summary>
        Bottom,
    }


    /// <summary>
    /// 放大后多出来的宽度往哪边推
    /// </summary>
    public enum Spread
    {
        /// <summary>左边固定，往右推（靠左对齐的一排）</summary>
        Right,
        /// <summary>右边固定，往左推（靠右对齐的一排）</summary>
        Left,
        /// <summary>往两边推（居中的一排，和 macOS 一样）</summary>
        Both,
    }


    private readonly UIElement _hoverArea;

    private readonly Func<IEnumerable<FrameworkElement>> _getItems;

    /// <summary>
    /// 正下方元素的最大缩放
    /// </summary>
    public float MaxScale { get; init; } = 1.5f;

    /// <summary>
    /// 影响半径，以元素宽度的倍数计；指针离元素中心超过这个距离就不放大
    /// </summary>
    public float RangeInItems { get; init; } = 2.5f;

    /// <summary>
    /// 元素自身固定在锚边上的内边距，例如图标上方还有一段容器的 Padding
    /// </summary>
    public float AnchorInset { get; init; }

    public Anchor ScaleAnchor { get; init; } = Anchor.Top;

    public Spread SpreadDirection { get; init; } = Spread.Right;

    /// <summary>
    /// 指针要在元素所在的这一横条内（上下各留这么多余量）才放大，移到下面一行的复选框上就收回
    /// </summary>
    public float VerticalMargin { get; init; } = 12;

    /// <summary>
    /// 暂停效果，例如拖动排序期间
    /// </summary>
    public bool IsSuspended
    {
        get; set
        {
            field = value;
            if (value)
            {
                _targetStrength = 0;
                EnsureRendering();
            }
        }
    }


    private readonly List<ItemState> _items = new();

    private double _pointerX;

    private double _pointerY;

    private float _strength;

    private float _targetStrength;

    private bool _rendering;



    public DockMagnifier(UIElement hoverArea, Func<IEnumerable<FrameworkElement>> getItems)
    {
        _hoverArea = hoverArea;
        _getItems = getItems;
        _hoverArea.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(OnPointerEntered), true);
        _hoverArea.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
        _hoverArea.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(OnPointerExited), true);
        _hoverArea.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnPointerExited), true);
        if (_hoverArea is FrameworkElement fe)
        {
            fe.Unloaded += (_, _) => Stop(reset: true);
        }
    }



    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // 每次移入都重新量一次元素的位置，元素可能增减、排序或改变了大小
        MeasureItems();
        OnPointerMoved(sender, e);
    }


    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!MotionSettings.IsEnabled || IsSuspended)
        {
            return;
        }
        var point = e.GetCurrentPoint(_hoverArea).Position;
        _pointerX = point.X;
        _pointerY = point.Y;
        if (_items.Count == 0)
        {
            MeasureItems();
        }
        _targetStrength = IsPointerInItemBand() ? 1 : 0;
        EnsureRendering();
    }


    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _targetStrength = 0;
        EnsureRendering();
    }



    /// <summary>
    /// 记录每个元素静止时的位置和大小。Visual 的变换不影响布局，所以量到的永远是原位
    /// </summary>
    private void MeasureItems()
    {
        try
        {
            var previous = new HashSet<FrameworkElement>();
            foreach (ItemState state in _items)
            {
                previous.Add(state.Element);
            }
            _items.Clear();
            foreach (FrameworkElement element in _getItems())
            {
                if (element.Visibility is not Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
                {
                    continue;
                }
                var origin = element.TransformToVisual(_hoverArea).TransformPoint(new Windows.Foundation.Point(0, 0));
                Visual visual = ElementCompositionPreview.GetElementVisual(element);
                ElementCompositionPreview.SetIsTranslationEnabled(element, true);
                _items.Add(new ItemState(element, visual, (float)origin.X, (float)origin.Y, (float)element.ActualWidth, (float)element.ActualHeight));
                previous.Remove(element);
            }
            // 已经不在列表里的元素（被移除或折叠）复原
            foreach (FrameworkElement element in previous)
            {
                ResetElement(element);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Measure dock items");
            _items.Clear();
        }
    }


    private bool IsPointerInItemBand()
    {
        if (_items.Count == 0)
        {
            return false;
        }
        float top = float.MaxValue, bottom = float.MinValue;
        foreach (ItemState item in _items)
        {
            top = Math.Min(top, item.Y);
            bottom = Math.Max(bottom, item.Y + item.Height);
        }
        return _pointerY >= top - VerticalMargin && _pointerY <= bottom + VerticalMargin;
    }



    private void EnsureRendering()
    {
        if (!_rendering)
        {
            _rendering = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }


    private void Stop(bool reset)
    {
        if (_rendering)
        {
            _rendering = false;
            CompositionTarget.Rendering -= OnRendering;
        }
        if (reset)
        {
            _strength = 0;
            _targetStrength = 0;
            foreach (ItemState item in _items)
            {
                ResetElement(item.Element);
            }
        }
    }



    private void OnRendering(object? sender, object e)
    {
        try
        {
            if (!MotionSettings.IsEnabled)
            {
                Stop(reset: true);
                return;
            }
            // 移入快一点，移出慢一点，收回时更柔和
            float rate = _targetStrength > _strength ? 0.25f : 0.16f;
            _strength += (_targetStrength - _strength) * rate;
            if (Math.Abs(_targetStrength - _strength) < 0.002f)
            {
                _strength = _targetStrength;
            }
            Apply();
            if (_strength == 0 && _targetStrength == 0)
            {
                Stop(reset: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dock magnifier");
            Stop(reset: true);
        }
    }



    private void Apply()
    {
        int count = _items.Count;
        if (count == 0)
        {
            return;
        }
        Span<float> scales = stackalloc float[count];
        Span<float> extras = stackalloc float[count];
        float totalExtra = 0;
        for (int i = 0; i < count; i++)
        {
            ItemState item = _items[i];
            float range = item.Width * RangeInItems;
            float distance = Math.Abs((float)_pointerX - (item.X + item.Width / 2));
            // 余弦衰减：正下方最大，到影响半径边缘平滑降到 0
            float falloff = distance < range ? (MathF.Cos(MathF.PI * distance / range) + 1) / 2 : 0;
            scales[i] = 1 + (MaxScale - 1) * falloff * _strength;
            extras[i] = item.Width * (scales[i] - 1);
            totalExtra += extras[i];
        }
        float before = 0;
        for (int i = 0; i < count; i++)
        {
            ItemState item = _items[i];
            // 以元素中心缩放时左右各多出 extra/2；再按方向把后面的元素推开，前后元素就不会重叠
            float after = totalExtra - before - extras[i];
            float offsetX = SpreadDirection switch
            {
                Spread.Right => before + extras[i] / 2,
                Spread.Left => -(after + extras[i] / 2),
                _ => before + extras[i] / 2 - totalExtra / 2,
            };
            float centerY = ScaleAnchor is Anchor.Top ? AnchorInset : item.Height - AnchorInset;
            item.Visual.CenterPoint = new Vector3(item.Width / 2, centerY, 0);
            item.Visual.Scale = new Vector3(scales[i], scales[i], 1);
            item.Visual.Properties.InsertVector3("Translation", new Vector3(offsetX, 0, 0));
            before += extras[i];
        }
    }



    private static void ResetElement(FrameworkElement element)
    {
        try
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            visual.Scale = Vector3.One;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
        catch { }
    }



    private sealed record ItemState(FrameworkElement Element, Visual Visual, float X, float Y, float Width, float Height);


}
