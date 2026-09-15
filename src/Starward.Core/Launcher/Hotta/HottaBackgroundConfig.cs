using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Hotta;

/// <summary>
/// 异环启动器背景目录里的 <c>config.json</c>，说明这一版该显示哪张图、哪段视频。
/// <para/>
/// 它自己也是清单里的一个文件，与背景素材放在同一个目录，
/// 因此不必猜文件名——猜会猜错：<c>discards</c> 里就列着已经作废但可能还留在
/// 本机的旧文件（例如 <c>bg_0.jpg</c>）。
/// </summary>
public class HottaBackgroundConfig
{

    /// <summary>
    /// 轮播的静态图。官方启动器会轮播，Starward 只取第一张。
    /// </summary>
    [JsonPropertyName("imgs")]
    public List<HottaBackgroundImage>? Images { get; set; }


    /// <summary>
    /// 动态背景的文件名，可以没有
    /// </summary>
    [JsonPropertyName("video")]
    public string? Video { get; set; }


    /// <summary>
    /// 没有视频时用的静态图，实际上也正是视频的首帧图
    /// </summary>
    [JsonPropertyName("noVideoBg")]
    public string? NoVideoBackground { get; set; }


    /// <summary>
    /// 已作废的文件名。本机目录里可能还留着它们，
    /// 靠「找最新的一个文件」去挑背景就会挑到这些。
    /// </summary>
    [JsonPropertyName("discards")]
    public List<string>? Discards { get; set; }


    /// <summary>
    /// 这一版该显示的静态图文件名，没有返回 null。
    /// <para/>
    /// 优先用 <see cref="NoVideoBackground"/>：它就是视频的首帧，
    /// 压在视频上算主题色、停播后当静态背景都是它最合适。
    /// </summary>
    public string? GetPosterFileName()
    {
        if (!string.IsNullOrWhiteSpace(NoVideoBackground))
        {
            return NoVideoBackground;
        }
        return Images?.Select(x => x.File).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    }

}


public class HottaBackgroundImage
{

    [JsonPropertyName("file")]
    public string? File { get; set; }


    /// <summary>
    /// 官方启动器压在图上的文字颜色，Starward 自己算主题色，不用它
    /// </summary>
    [JsonPropertyName("tipcolor")]
    public string? TipColor { get; set; }

}


[JsonSerializable(typeof(HottaBackgroundConfig))]
internal partial class HottaLauncherJsonContext : JsonSerializerContext
{

}
