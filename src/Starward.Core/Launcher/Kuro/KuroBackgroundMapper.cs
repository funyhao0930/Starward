using Starward.Core.HoYoPlay;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 把鸣潮的背景图配置换成应用统一使用的 <see cref="GameBackground"/>。
/// <para/>
/// 单独放一层是因为这段映射有不少边界情况（视频缺图、开关关闭），
/// 而它不依赖任何 IO，值得单独测。
/// </summary>
public static class KuroBackgroundMapper
{

    /// <summary>
    /// 换成统一的背景图列表，没有可用背景时返回空列表。
    /// <para/>
    /// 鸣潮一次只给一张背景，因此列表最多一项。
    /// </summary>
    public static List<GameBackground> ToGameBackgrounds(KuroLauncherBackground? background)
    {
        if (background is null || background.FunctionSwitch != KuroLauncherBackground.FUNCTION_ON)
        {
            return [];
        }
        if (background.BackgroundFileType is KuroLauncherBackground.FILE_TYPE_VIDEO)
        {
            string? video = background.BackgroundFile;
            string? poster = background.FirstFrameImage;
            string? slogan = background.Slogan;
            // 视频背景至少要有视频和首帧图：显示层拿首帧图算主题色，也拿它当停播后的静态背景。
            // 标语图是可选的，显示层允许没有叠图。
            if (!string.IsNullOrWhiteSpace(video) && !string.IsNullOrWhiteSpace(poster))
            {
                return
                [
                    new GameBackground
                    {
                        Id = MakeId(video),
                        Type = GameBackground.BACKGROUND_TYPE_VIDEO,
                        Background = new GameImage { Url = poster },
                        Video = new GameImage { Url = video },
                        Theme = string.IsNullOrWhiteSpace(slogan) ? null : new GameImage { Url = slogan },
                    },
                ];
            }
            // 缺了视频或首帧图就凑不成动态背景，有首帧图还能当静态图用
            return FromStillImage(poster);
        }
        // 不是视频时 backgroundFile 本身就是那张图
        return FromStillImage(background.BackgroundFile ?? background.FirstFrameImage);
    }


    private static List<GameBackground> FromStillImage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return [];
        }
        return
        [
            new GameBackground
            {
                Id = MakeId(url),
                Type = GameBackground.BACKGROUND_TYPE_UNSPECIFIED,
                Background = new GameImage { Url = url },
            },
        ];
    }


    /// <summary>
    /// 鸣潮的接口没有米哈游那样的背景图 ID，但 CDN 文件名是随美术一起换的，
    /// 拿它当 ID 足够让上层判断「换了没有」。
    /// </summary>
    private static string MakeId(string url) => Path.GetFileNameWithoutExtension(url);

}
