using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Features.Gacha;

/// <summary>
/// 按 <see cref="GachaLogItemEx.RarityTier"/>（0 = 该游戏的最高稀有度）给通用物品统计格配色：
/// 金、紫、蓝，其余灰。ConverterParameter 选 Background（上亮下暗的渐变底）、Border 或 Foreground。
/// 鸣潮、终末地、异环的最高星级不同，用层级而不是星级配色，三款就能共用一套。
/// </summary>
internal partial class RarityTierToBrushConverter : IValueConverter
{

    private static readonly Color[] Colors =
    [
        Color.FromArgb(0xFF, 0xF2, 0xB8, 0x4B),
        Color.FromArgb(0xFF, 0xC0, 0x84, 0xF0),
        Color.FromArgb(0xFF, 0x6F, 0xAE, 0xF0),
        Color.FromArgb(0xFF, 0x9A, 0xA4, 0xB0),
    ];


    public object Convert(object value, Type targetType, object parameter, string language)
    {
        int tier = value is int i ? Math.Clamp(i, 0, Colors.Length - 1) : Colors.Length - 1;
        Color color = Colors[tier];
        return (parameter as string) switch
        {
            "Border" => new SolidColorBrush(Color.FromArgb(0x66, color.R, color.G, color.B)),
            "Foreground" => new SolidColorBrush(color),
            _ => new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Offset = 0, Color = Darken(color, 0.55) },
                    new GradientStop { Offset = 1, Color = Darken(color, 0.22) },
                },
            },
        };
    }


    private static Color Darken(Color color, double factor)
    {
        return Color.FromArgb(0xFF, (byte)(color.R * factor), (byte)(color.G * factor), (byte)(color.B * factor));
    }


    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }

}
