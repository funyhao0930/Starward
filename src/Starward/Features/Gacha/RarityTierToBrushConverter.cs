using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Features.Gacha;

/// <summary>
/// 按 <see cref="GachaLogItemEx.RarityTier"/>（0 = 该游戏的最高稀有度）给通用物品统计格配色。
/// 各游戏的最高星级不同（终末地 6 星、其他 5 星），按层级而不是星级取色。
/// <para/>
/// <see cref="Palette"/> 选游戏风格：
/// Kuro（鸣潮）金紫蓝的上亮下暗渐层；
/// Endfield（终末地）官网的黑底直角，稀有度色只用在角标与色带；
/// Nte（异环）都市霓虹感的对角渐层。
/// ConverterParameter 选 Background、Border、Foreground。
/// </summary>
internal partial class RarityTierToBrushConverter : IValueConverter
{

    public string Palette { get; set; } = "Kuro";


    private static readonly Color[] KuroColors =
    [
        Hex(0xF2B84B),
        Hex(0xC084F0),
        Hex(0x6FAEF0),
        Hex(0x9AA4B0),
    ];

    /// <summary>
    /// 终末地：6 星橘、5 星黄、4 星紫、3 星蓝
    /// </summary>
    private static readonly Color[] EndfieldColors =
    [
        Hex(0xFF7A1A),
        Hex(0xFFD23F),
        Hex(0xB07CFF),
        Hex(0x4DA6FF),
        Hex(0x8A8A8A),
    ];

    /// <summary>
    /// 异环：S 金粉、A 紫蓝、B 天蓝，每一级是一对渐层色
    /// </summary>
    private static readonly (Color Start, Color End)[] NteColors =
    [
        (Hex(0xFFB547), Hex(0xFF5E7E)),
        (Hex(0xB678FF), Hex(0x5B5BFF)),
        (Hex(0x57C7FF), Hex(0x3F7BFF)),
        (Hex(0x9AA4B0), Hex(0x5C6470)),
    ];



    public object Convert(object value, Type targetType, object parameter, string language)
    {
        int tier = value is int i ? i : int.MaxValue;
        string part = parameter as string ?? "Background";
        return Palette switch
        {
            "Endfield" => ConvertEndfield(Pick(EndfieldColors, tier), part),
            "Nte" => ConvertNte(Pick(NteColors, tier), part),
            _ => ConvertKuro(Pick(KuroColors, tier), part),
        };
    }


    private static Brush ConvertKuro(Color color, string part) => part switch
    {
        "Border" => new SolidColorBrush(WithAlpha(color, 0x66)),
        "Foreground" => new SolidColorBrush(color),
        _ => Gradient(new Point(0, 0), new Point(0, 1), Darken(color, 0.55), Darken(color, 0.22)),
    };


    private static Brush ConvertEndfield(Color color, string part) => part switch
    {
        "Border" => new SolidColorBrush(WithAlpha(color, 0x33)),
        "Foreground" => new SolidColorBrush(color),
        // 底色统一是官网的黑，只在最上面透出一点稀有度色
        _ => Gradient(new Point(0, 0), new Point(0, 1), Blend(Hex(0x191919), color, 0.14), Hex(0x141414)),
    };


    private static Brush ConvertNte((Color Start, Color End) colors, string part) => part switch
    {
        "Border" => new SolidColorBrush(WithAlpha(colors.Start, 0x80)),
        "Foreground" => new SolidColorBrush(colors.Start),
        _ => new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop { Offset = 0, Color = Darken(colors.Start, 0.85) },
                new GradientStop { Offset = 0.5, Color = Darken(colors.End, 0.55) },
                new GradientStop { Offset = 1, Color = Hex(0x1B1530) },
            },
        },
    };



    private static T Pick<T>(T[] colors, int tier) => colors[Math.Clamp(tier, 0, colors.Length - 1)];

    private static Color Hex(uint rgb) => Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Darken(Color color, double factor) => Color.FromArgb(0xFF, (byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(0xFF, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static LinearGradientBrush Gradient(Point start, Point end, Color from, Color to) => new()
    {
        StartPoint = start,
        EndPoint = end,
        GradientStops =
        {
            new GradientStop { Offset = 0, Color = from },
            new GradientStop { Offset = 1, Color = to },
        },
    };


    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }

}
