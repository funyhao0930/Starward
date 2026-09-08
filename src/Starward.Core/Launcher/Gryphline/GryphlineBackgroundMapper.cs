using Starward.Core.HoYoPlay;

namespace Starward.Core.Launcher.Gryphline;

/// <summary>
/// 把 GRYPHLINK 的背景图换成应用统一使用的 <see cref="GameBackground"/>。
/// <para/>
/// 与 <c>KuroBackgroundMapper</c> 同样的理由单独放一层：映射有边界情况，
/// 而它不依赖任何 IO，值得单独测。
/// </summary>
public static class GryphlineBackgroundMapper
{

    /// <summary>
    /// 换成统一的背景图列表，没有可用背景时返回空列表。
    /// <para/>
    /// 接口一次只给一张背景，因此列表最多一项。
    /// </summary>
    public static List<GameBackground> ToGameBackgrounds(GryphlineMainBgImage? image)
    {
        string? poster = image?.Url;
        if (string.IsNullOrWhiteSpace(poster))
        {
            // 没有静态图就凑不成背景：显示层拿它算主题色，停播视频后也要靠它
            return [];
        }
        string id = MakeId(image!);
        if (!string.IsNullOrWhiteSpace(image!.VideoUrl))
        {
            return
            [
                new GameBackground
                {
                    Id = id,
                    Type = GameBackground.BACKGROUND_TYPE_VIDEO,
                    Background = new GameImage { Url = poster },
                    Video = new GameImage { Url = image.VideoUrl },
                    // 这家不给版本标语，动态背景上没有叠图
                    Theme = null,
                },
            ];
        }
        return
        [
            new GameBackground
            {
                Id = id,
                Type = GameBackground.BACKGROUND_TYPE_UNSPECIFIED,
                Background = new GameImage { Url = poster },
            },
        ];
    }


    /// <summary>
    /// 接口没有背景图 ID，但给了静态图的 md5，它比文件名更适合判断「换了没有」。
    /// 万一没有 md5 再退回文件名。
    /// </summary>
    private static string MakeId(GryphlineMainBgImage image)
    {
        if (!string.IsNullOrWhiteSpace(image.Md5))
        {
            return image.Md5;
        }
        return Path.GetFileNameWithoutExtension(image.Url) ?? string.Empty;
    }

}
