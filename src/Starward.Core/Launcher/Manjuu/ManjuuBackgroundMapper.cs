using Starward.Core.HoYoPlay;

namespace Starward.Core.Launcher.Manjuu;

/// <summary>
/// 把蓝色星原官方启动器缓存的美术换成应用统一使用的 <see cref="GameBackground"/>。
/// <para/>
/// 单独放一层是因为这段映射不依赖 IO，值得单独测。
/// </summary>
public static class ManjuuBackgroundMapper
{

    /// <summary>
    /// 放进 Starward 背景缓存目录时的文件名前缀，避免与其他游戏的文件撞名
    /// </summary>
    private const string CachePrefix = "manjuu_";


    /// <summary>
    /// 启动器的背景图是照它自己的窗口形状做的：上方有一条全透明的标题栏缺口，四角是圆角。
    /// 去掉透明部分后再往内收这么多像素，把圆角也切掉。
    /// </summary>
    public const int CornerInset = 16;


    /// <summary>
    /// 静态背景在 Starward 缓存里的文件名，没有可用的图时返回 null。
    /// <para/>
    /// 用媒体缓存的文件名（如 <c>bg_1795526199039</c>）而不是 CDN 文件名：后者带中文与全角括号。
    /// 一律存成 png，因为显示前会先裁掉透明的边。
    /// </summary>
    public static string? GetPosterCacheName(ManjuuLauncherCache? cache)
    {
        string? name = FirstNonEmpty(cache?.BackgroundName, FileNameOf(cache?.BackgroundUrl));
        return name is null ? null : $"{CachePrefix}{Path.GetFileNameWithoutExtension(name)}.png";
    }


    /// <summary>
    /// 动态背景在 Starward 缓存里的文件名，没有视频时返回 null。
    /// 媒体缓存里的扩展名是 .dat，内容其实是 mp4，存进来时改回 .mp4，显示层才认得是视频。
    /// </summary>
    public static string? GetVideoCacheName(ManjuuLauncherCache? cache)
    {
        string? name = FirstNonEmpty(cache?.VideoName, FileNameOf(cache?.VideoUrl));
        return name is null ? null : $"{CachePrefix}{Path.GetFileNameWithoutExtension(name)}.mp4";
    }


    /// <summary>
    /// 换成统一的背景，拿不到静态图时返回 null。
    /// </summary>
    /// <param name="posterCacheName">已经放进缓存目录的静态图文件名</param>
    /// <param name="videoCacheName">已经放进缓存目录的视频文件名，没有就传 null</param>
    public static GameBackground? ToGameBackground(ManjuuLauncherCache? cache, string? posterCacheName, string? videoCacheName)
    {
        // 没有静态图就凑不出背景：显示层要拿它算主题色，也要拿它当停播后的静态背景
        if (cache is null || string.IsNullOrWhiteSpace(posterCacheName))
        {
            return null;
        }
        if (!string.IsNullOrWhiteSpace(videoCacheName))
        {
            return new GameBackground
            {
                Id = MakeId(cache.VideoUrl ?? videoCacheName),
                Type = GameBackground.BACKGROUND_TYPE_VIDEO,
                Background = new GameImage { Url = posterCacheName },
                Video = new GameImage { Url = videoCacheName },
                // 官方启动器的视频上没有另外叠的标语图
                Theme = null,
            };
        }
        return new GameBackground
        {
            Id = MakeId(cache.BackgroundUrl ?? posterCacheName),
            Type = GameBackground.BACKGROUND_TYPE_UNSPECIFIED,
            Background = new GameImage { Url = posterCacheName },
        };
    }


    /// <summary>
    /// 算出要保留的区域，不需要裁时返回 null。
    /// <para/>
    /// 沿着横竖两条中线找第一个与最后一个完全不透明的像素，得到不透明的范围；
    /// 再往内收 <see cref="CornerInset"/> 切掉圆角。四角与中线全都不透明时说明美术没有挖空，原样使用。
    /// </summary>
    /// <param name="bgra">逐行排列的 BGRA8 像素，每像素 4 字节</param>
    public static (int X, int Y, int Width, int Height)? GetCropRect(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
        {
            return null;
        }

        bool Opaque(int x, int y, ReadOnlySpan<byte> pixels) => pixels[(y * width + x) * 4 + 3] == 255;

        int midX = width / 2, midY = height / 2;
        int top = 0, bottom = height - 1, left = 0, right = width - 1;
        while (top < height && !Opaque(midX, top, bgra)) top++;
        while (bottom > top && !Opaque(midX, bottom, bgra)) bottom--;
        while (left < width && !Opaque(left, midY, bgra)) left++;
        while (right > left && !Opaque(right, midY, bgra)) right--;
        if (top >= height || left >= width)
        {
            // 整条中线都透明，不是能用的背景
            return null;
        }

        bool cornersOpaque = Opaque(0, 0, bgra) && Opaque(width - 1, 0, bgra) && Opaque(0, height - 1, bgra) && Opaque(width - 1, height - 1, bgra);
        if (cornersOpaque && top == 0 && left == 0 && bottom == height - 1 && right == width - 1)
        {
            return null;
        }

        int x = left + CornerInset, y = top + CornerInset;
        int w = right - left + 1 - CornerInset * 2, h = bottom - top + 1 - CornerInset * 2;
        if (w <= 0 || h <= 0)
        {
            return null;
        }
        return (x, y, w, h);
    }


    /// <summary>
    /// 启动器没有背景 ID，CDN 文件名是随美术一起换的，拿它当 ID 足够让上层判断「换了没有」
    /// </summary>
    private static string MakeId(string urlOrName) => Path.GetFileNameWithoutExtension(FileNameOf(urlOrName) ?? urlOrName);


    private static string? FileNameOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }
        string path = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.AbsolutePath : url;
        string name = Path.GetFileName(Uri.UnescapeDataString(path));
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }


    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return null;
    }

}
