using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Core.Games.Hotta;
using Starward.Core.Games.Kuro;
using Starward.Core.HoYoPlay;
using System;
using System.Globalization;
using Windows.UI;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 公告板的外观，每款游戏照自己游戏内或官网的公告样式配一套。
/// <para/>
/// 米哈游的公告是官方网页，样式是现成的；其他几家由 <see cref="VendorNoticeWindow"/> 自己画，
/// 为了打开时一眼认得出是哪款游戏，颜色、圆角、分页的样子都跟着游戏走：
/// 鸣潮取自游戏内公告板（米白纸底、黑色分页牌、金色选中、红色菱形提示），
/// 终末地取自官网（#FFFA00 亮黄、#191919 黑、直角、宽体英文小标），
/// 异环取自官网（#1D1D1D 黑、#7CECFC 青、#FB5692 粉、粗黑边框）。
/// </summary>
public sealed class VendorNoticeTheme
{

    /// <summary>
    /// 公告板外面的暗处
    /// </summary>
    public Color Overlay { get; init; } = Hex("#80000000");

    public Color BoardBackground { get; init; } = Hex("#F3F3F3");

    public CornerRadius BoardCornerRadius { get; init; } = new(8);

    public Color BoardBorder { get; init; } = Hex("#00000000");

    public Thickness BoardBorderThickness { get; init; } = new(0);


    public Color HeaderBackground { get; init; } = Hex("#EBEBEB");

    public Color HeaderBorder { get; init; } = Hex("#00000000");

    public Thickness HeaderBorderThickness { get; init; } = new(0);

    public Color CloseForeground { get; init; } = Hex("#1A1A1A");

    public HorizontalAlignment TabAlignment { get; init; } = HorizontalAlignment.Center;


    public Color TabBackground { get; init; } = Hex("#00000000");

    public Color TabHoverBackground { get; init; } = Hex("#0F000000");

    public Color TabForeground { get; init; } = Hex("#5C5C5C");

    public Color TabSelectedBackground { get; init; } = Hex("#00000000");

    public Color TabSelectedForeground { get; init; } = Hex("#1A1A1A");

    /// <summary>
    /// 选中分页底下的横条，透明表示不画
    /// </summary>
    public Color TabIndicator { get; init; } = Hex("#1A1A1A");

    public CornerRadius TabCornerRadius { get; init; } = new(4);

    public double TabMinWidth { get; init; } = 72;

    public Thickness TabMargin { get; init; } = new(2, 0, 2, 0);

    public FontFamily TabFontFamily { get; init; } = new("Segoe UI Variable Text");

    /// <summary>
    /// 分页名下方的英文小标，终末地的界面到处是这种双语标签
    /// </summary>
    public bool ShowTabSubtitle { get; init; }


    public Color ListBackground { get; init; } = Hex("#EDEDED");

    public Thickness ListItemMargin { get; init; } = new(6, 1, 6, 1);

    public CornerRadius ListItemCornerRadius { get; init; } = new(4);

    public Color ItemBackground { get; init; } = Hex("#00000000");

    public Color ItemHoverBackground { get; init; } = Hex("#0F000000");

    public Color ItemSelectedBackground { get; init; } = Hex("#FFFFFF");

    public Color ItemForeground { get; init; } = Hex("#1A1A1A");

    public Color ItemSecondaryForeground { get; init; } = Hex("#707070");

    public Color ItemSelectedForeground { get; init; } = Hex("#1A1A1A");

    public Color ItemSelectedSecondaryForeground { get; init; } = Hex("#707070");

    /// <summary>
    /// 选中项左边的竖条
    /// </summary>
    public Color ItemSelectedMarker { get; init; } = Hex("#1A1A1A");


    public Color RedDot { get; init; } = Hex("#E5484D");

    public NoticeDotShape RedDotShape { get; init; } = NoticeDotShape.Circle;


    /// <summary>
    /// 正文区的底色，网页本身是透明的
    /// </summary>
    public Color ContentBackground { get; init; } = Hex("#F7F7F7");

    /// <summary>
    /// 载入圈等强调色
    /// </summary>
    public Color Accent { get; init; } = Hex("#1A1A1A");

    /// <summary>
    /// 正文网页的样式，接在通用样式之后，可以覆盖通用样式
    /// </summary>
    public string ContentCss { get; init; } = "";



    /// <summary>
    /// 各游戏的样式，没有专属样式的用通用的一套
    /// </summary>
    public static VendorNoticeTheme ForGame(GameKey key)
    {
        return key.ProviderId switch
        {
            KuroGameMapping.ProviderId => WutheringWaves,
            GryphlineGameMapping.ProviderId => Endfield,
            HottaGameMapping.ProviderId => NevernessToEverness,
            _ => Default,
        };
    }


    public static VendorNoticeTheme Default { get; } = new()
    {
        ContentCss = """
            :root { --fg: #1A1A1A; --muted: #707070; --link: #0067C0; --thumb: #00000040; --rule: #D0D0D0; }
            """,
    };


    /// <summary>
    /// 鸣潮：游戏内公告板是米白纸底，分页是黑色的牌子，选中的那张变成金色；提示是红色菱形
    /// </summary>
    public static VendorNoticeTheme WutheringWaves { get; } = new()
    {
        Overlay = Hex("#99000000"),
        BoardBackground = Hex("#F2EFE8"),
        BoardCornerRadius = new(2),
        HeaderBackground = Hex("#F2EFE8"),
        HeaderBorder = Hex("#D9CFB9"),
        HeaderBorderThickness = new(0, 0, 0, 1),
        CloseForeground = Hex("#2B2B2B"),
        TabAlignment = HorizontalAlignment.Left,
        TabBackground = Hex("#1B1B1B"),
        TabHoverBackground = Hex("#333333"),
        TabForeground = Hex("#F2EFE8"),
        TabSelectedBackground = Hex("#C4A46B"),
        TabSelectedForeground = Hex("#1B1B1B"),
        TabIndicator = Hex("#00000000"),
        TabCornerRadius = new(2),
        TabMinWidth = 150,
        TabMargin = new(0, 0, 8, 0),
        ListBackground = Hex("#E8E3D8"),
        ListItemMargin = new(8, 2, 8, 2),
        ListItemCornerRadius = new(2),
        ItemHoverBackground = Hex("#F0ECE3"),
        ItemSelectedBackground = Hex("#FFFFFF"),
        ItemForeground = Hex("#2B2B2B"),
        ItemSecondaryForeground = Hex("#8C8577"),
        ItemSelectedForeground = Hex("#8F6E3A"),
        ItemSelectedSecondaryForeground = Hex("#A8916A"),
        ItemSelectedMarker = Hex("#C4A46B"),
        RedDot = Hex("#D0373B"),
        RedDotShape = NoticeDotShape.Diamond,
        ContentBackground = Hex("#FBFAF6"),
        Accent = Hex("#C4A46B"),
        ContentCss = """
            :root { --fg: #2E2E2E; --muted: #A8916A; --link: #A8823F; --thumb: #C4A46B80; --rule: #E2D8C3; }
            .sw-title { color: #1B1B1B; padding-bottom: 10px; border-bottom: 1px solid #E2D8C3; position: relative; }
            .sw-title::after { content: ""; position: absolute; left: 0; bottom: -2px; width: 72px; height: 3px; background: #C4A46B; }
            .sw-date { margin-top: 10px; }
            .sw-banner { border-radius: 2px; }
            """,
    };


    /// <summary>
    /// 终末地：官网的亮黄配黑，直角，分页名下面带宽体英文小标
    /// </summary>
    public static VendorNoticeTheme Endfield { get; } = new()
    {
        Overlay = Hex("#B3000000"),
        BoardBackground = Hex("#F2F2F2"),
        BoardCornerRadius = new(0),
        HeaderBackground = Hex("#191919"),
        HeaderBorder = Hex("#FFFA00"),
        HeaderBorderThickness = new(0, 0, 0, 3),
        CloseForeground = Hex("#FFFFFF"),
        TabAlignment = HorizontalAlignment.Left,
        TabBackground = Hex("#00000000"),
        TabHoverBackground = Hex("#2E2E2E"),
        TabForeground = Hex("#BFBFBF"),
        TabSelectedBackground = Hex("#FFFA00"),
        TabSelectedForeground = Hex("#191919"),
        TabIndicator = Hex("#00000000"),
        TabCornerRadius = new(0),
        TabMinWidth = 132,
        TabMargin = new(0),
        TabFontFamily = new("Bahnschrift"),
        ShowTabSubtitle = true,
        ListBackground = Hex("#E6E6E6"),
        ListItemMargin = new(0),
        ListItemCornerRadius = new(0),
        ItemHoverBackground = Hex("#D9D9D9"),
        ItemSelectedBackground = Hex("#191919"),
        ItemForeground = Hex("#191919"),
        ItemSecondaryForeground = Hex("#797979"),
        ItemSelectedForeground = Hex("#FFFFFF"),
        ItemSelectedSecondaryForeground = Hex("#BFBFBF"),
        ItemSelectedMarker = Hex("#FFFA00"),
        RedDot = Hex("#FF5A1F"),
        RedDotShape = NoticeDotShape.Square,
        ContentBackground = Hex("#FAFAFA"),
        Accent = Hex("#191919"),
        ContentCss = """
            :root { --fg: #191919; --muted: #797979; --link: #191919; --thumb: #19191960; --rule: #D9D9D9; }
            a { text-decoration-color: #E6DD00; text-decoration-thickness: 2px; text-underline-offset: 3px; }
            .sw-title { font-family: Bahnschrift, "Microsoft JhengHei UI", sans-serif; border-left: 6px solid #FFFA00; padding: 2px 0 2px 12px; }
            .sw-date { font-family: Bahnschrift, sans-serif; letter-spacing: 1px; padding-left: 18px; }
            .sw-banner { border-radius: 0; }
            """,
    };


    /// <summary>
    /// 异环：官网的黑白粗框，选中用青色，提示用粉色
    /// </summary>
    public static VendorNoticeTheme NevernessToEverness { get; } = new()
    {
        Overlay = Hex("#B3000000"),
        BoardBackground = Hex("#E8E8E8"),
        BoardCornerRadius = new(0),
        BoardBorder = Hex("#1D1D1D"),
        BoardBorderThickness = new(3),
        HeaderBackground = Hex("#1D1D1D"),
        CloseForeground = Hex("#FFFFFF"),
        TabAlignment = HorizontalAlignment.Left,
        TabBackground = Hex("#00000000"),
        TabHoverBackground = Hex("#313131"),
        TabForeground = Hex("#AAAAAA"),
        TabSelectedBackground = Hex("#00000000"),
        TabSelectedForeground = Hex("#7CECFC"),
        TabIndicator = Hex("#7CECFC"),
        TabCornerRadius = new(0),
        TabMinWidth = 96,
        TabMargin = new(0, 0, 4, 0),
        ListBackground = Hex("#F1F1F1"),
        ListItemMargin = new(0),
        ListItemCornerRadius = new(0),
        ItemHoverBackground = Hex("#E2E2E2"),
        ItemSelectedBackground = Hex("#1D1D1D"),
        ItemForeground = Hex("#313131"),
        ItemSecondaryForeground = Hex("#777474"),
        ItemSelectedForeground = Hex("#FFFFFF"),
        ItemSelectedSecondaryForeground = Hex("#AAAAAA"),
        ItemSelectedMarker = Hex("#7CECFC"),
        RedDot = Hex("#FB5692"),
        RedDotShape = NoticeDotShape.Circle,
        ContentBackground = Hex("#FFFFFF"),
        Accent = Hex("#1D1D1D"),
        ContentCss = """
            :root { --fg: #313131; --muted: #777474; --link: #E51737; --thumb: #31313160; --rule: #DEDEDE; }
            .sw-title { color: #1D1D1D; padding-bottom: 10px; border-bottom: 3px solid #313131; }
            .sw-date { margin-top: 10px; }
            .sw-banner { border-radius: 0; }
            """,
    };



    /// <summary>
    /// 分页的英文小标，只有 <see cref="ShowTabSubtitle"/> 时显示
    /// </summary>
    public static string GetTabSubtitle(string postType)
    {
        return postType switch
        {
            GamePostType.POST_TYPE_ANNOUNCE => "UPDATES",
            GamePostType.POST_TYPE_ACTIVITY => "EVENTS",
            GamePostType.POST_TYPE_INFO => "NEWS",
            _ => "",
        };
    }


    /// <summary>
    /// <c>#RRGGBB</c> 或 <c>#AARRGGBB</c>
    /// </summary>
    private static Color Hex(string hex)
    {
        ReadOnlySpan<char> s = hex.AsSpan().TrimStart('#');
        uint value = uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (s.Length == 6)
        {
            value |= 0xFF000000;
        }
        return Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

}



/// <summary>
/// 红点的形状
/// </summary>
public enum NoticeDotShape
{
    Circle,
    Square,
    Diamond,
}
