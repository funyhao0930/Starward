using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;

namespace Starward.Helpers;

/// <summary>
/// 页面内容由上而下依次浮上来，大号数字从 0 滚到实际值。
/// 设置页、工具箱、游戏设置页都是「ScrollViewer 里一个 Panel 装着一块块内容」，共用这一套。
/// </summary>
public static class PageStagger
{

    private static readonly ILogger _logger = AppConfig.GetLogger<PageStaggerLog>();

    private sealed class PageStaggerLog { }


    /// <summary>
    /// 不用 Frame 内置的转场导航，新页面先藏起来，布局好之后内容依次浮上
    /// </summary>
    public static bool NavigateWithStagger(this Frame frame, Type page, object? parameter = null, bool countUp = false)
    {
        bool result = frame.Navigate(page, parameter, new SuppressNavigationTransitionInfo());
        if (frame.Content is FrameworkElement content && MotionSettings.IsEnabled)
        {
            // 先藏起来，等页面布局好再一项项浮上来，免得先闪一帧完整页面
            content.HideUntilEnter();
            void OnLoaded(object sender, RoutedEventArgs e)
            {
                content.Loaded -= OnLoaded;
                content.ResetMotion();
                content.StaggerContent(countUp);
            }
            content.Loaded += OnLoaded;
        }
        return result;
    }


    /// <summary>
    /// 把页面里装内容的 Panel 的子元素依次浮上来；<paramref name="countUp"/> 时大号数字也滚上来
    /// </summary>
    public static void StaggerContent(this FrameworkElement page, bool countUp = false)
    {
        if (!MotionSettings.IsEnabled)
        {
            return;
        }
        try
        {
            if (FindItemsPanel(page) is Panel panel)
            {
                var children = new List<UIElement>();
                foreach (UIElement child in panel.Children)
                {
                    if (child.Visibility is Visibility.Visible)
                    {
                        children.Add(child);
                    }
                }
                _ = children.StaggerInAsync(offsetY: 12);
            }
            else
            {
                _ = page.EnterAsync(offsetY: 12);
            }
            if (countUp)
            {
                CountUpLargeNumbers(page);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stagger page content");
        }
    }


    /// <summary>
    /// 找装内容的 Panel：优先取第一个 ScrollViewer 的内容。
    /// 不能直接找子元素多的 Panel，ScrollViewer 模板里放滚动条的 Grid 会先被找到
    /// </summary>
    private static Panel? FindItemsPanel(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is ScrollViewer { Content: Panel content } && content.Children.Count >= 2)
            {
                return content;
            }
            if (node is StackPanel stack && stack.Children.Count >= 3)
            {
                return stack;
            }
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(node, i));
            }
        }
        return null;
    }


    /// <summary>
    /// 大号（20 以上）的数字文字从 0 滚上来。工具箱的数据多半在页面载入后才查回来，
    /// 所以还没有数字的文字会等两秒内第一次被填上数字时再滚
    /// </summary>
    private static void CountUpLargeNumbers(DependencyObject root)
    {
        long start = Environment.TickCount64;
        foreach (TextBlock textBlock in FindLargeTextBlocks(root))
        {
            if (HasDigit(textBlock.Text))
            {
                textBlock.CountUp();
                continue;
            }
            long token = 0;
            token = textBlock.RegisterPropertyChangedCallback(TextBlock.TextProperty, (sender, _) =>
            {
                var tb = (TextBlock)sender;
                tb.UnregisterPropertyChangedCallback(TextBlock.TextProperty, token);
                if (Environment.TickCount64 - start < 2000 && HasDigit(tb.Text))
                {
                    tb.CountUp();
                }
            });
        }
    }


    private static IEnumerable<TextBlock> FindLargeTextBlocks(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock { FontSize: >= 20 } textBlock)
            {
                yield return textBlock;
            }
            foreach (TextBlock nested in FindLargeTextBlocks(child))
            {
                yield return nested;
            }
        }
    }


    private static bool HasDigit(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }
        foreach (char c in text)
        {
            if (char.IsAsciiDigit(c))
            {
                return true;
            }
        }
        return false;
    }

}
