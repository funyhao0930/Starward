using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Helpers;
using System;
using System.Linq;


namespace Starward.Features.Gacha;

public sealed partial class GachaStatsCard : UserControl
{

    public GachaStatsCard()
    {
        this.InitializeComponent();
    }



    public GachaTypeStats WarpTypeStats
    {
        get { return (GachaTypeStats)GetValue(WarpTypeStatsProperty); }
        set { SetValue(WarpTypeStatsProperty, value); }
    }

    // Using a DependencyProperty as the backing store for WarpTypeStats.  This enables animation, styling, binding, etc...
    /// <summary>
    /// 卡片载入的时间，载入后一小段时间内出现的保底进度条才从左边长出来，
    /// 列表滚动时重新生成的不播，免得一直闪
    /// </summary>
    private readonly long _loadedAt = Environment.TickCount64;

    private int _pityBarIndex;


    private void PityBar_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement bar && Environment.TickCount64 - _loadedAt < 1500)
        {
            TimeSpan delay = TimeSpan.FromMilliseconds(150 + 25 * Math.Min(_pityBarIndex++, 12));
            _ = bar.GrowFromLeftAsync(delay);
        }
    }


    public static readonly DependencyProperty WarpTypeStatsProperty =
        DependencyProperty.Register("WarpTypeStats", typeof(GachaTypeStats), typeof(GachaStatsCard), new PropertyMetadata(null));


    private void Grid_Rarity5Item_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement ele && ele.Tag is GachaLogItemEx item)
            {
                if (WarpTypeStats?.List_5?.Any() ?? false)
                {
                    foreach (var l5 in WarpTypeStats.List_5)
                    {
                        l5.IsPointerIn = (l5.Name == item.Name);
                    }
                }
            }
        }
        catch { }
    }


    private void Grid_Rarity5Item_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement ele && ele.Tag is GachaLogItemEx item)
            {
                if (WarpTypeStats?.List_5?.Any() ?? false)
                {
                    foreach (var l5 in WarpTypeStats.List_5)
                    {
                        l5.IsPointerIn = false;
                    }
                }
            }
        }
        catch { }
    }


    private void Grid_Rarity4Item_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement ele && ele.Tag is GachaLogItemEx item)
            {
                if (WarpTypeStats?.List_4?.Any() ?? false)
                {
                    foreach (var l5 in WarpTypeStats.List_4)
                    {
                        l5.IsPointerIn = (l5.Name == item.Name);
                    }
                }
            }
        }
        catch { }
    }


    private void Grid_Rarity4Item_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement ele && ele.Tag is GachaLogItemEx item)
            {
                if (WarpTypeStats?.List_4?.Any() ?? false)
                {
                    foreach (var l5 in WarpTypeStats.List_4)
                    {
                        l5.IsPointerIn = false;
                    }
                }
            }
        }
        catch { }
    }


    private void TextBlock_GachaTypeText_IsTextTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        //if (sender.FontSize == 16)
        //{
        //    sender.FontSize = 14;
        //}
        //if (sender.FontSize == 14)
        //{
        //    sender.FontSize = 12;
        //}
    }


    public void ResetGachaTypeTextFontSize()
    {
        TextBlock_GachaTypeText.FontSize = 16;
    }

}
