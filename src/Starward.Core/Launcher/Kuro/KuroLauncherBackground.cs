using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 鸣潮官方启动器首页的背景图配置。
/// <para/>
/// 一份配置只描述一张背景，没有米哈游那样的多张列表，
/// 也没有服务器端的背景图 ID，换没换只能看 CDN 文件名。
/// </summary>
public class KuroLauncherBackground
{

    /// <summary>
    /// 功能开关，不是 <see cref="FUNCTION_ON"/> 时官方启动器不显示背景
    /// </summary>
    [JsonPropertyName("functionSwitch")]
    public int FunctionSwitch { get; set; }


    /// <summary>
    /// 背景文件，按 <see cref="BackgroundFileType"/> 决定是视频还是图片
    /// </summary>
    [JsonPropertyName("backgroundFile")]
    public string? BackgroundFile { get; set; }


    /// <summary>
    /// <see cref="FILE_TYPE_IMAGE"/> 或 <see cref="FILE_TYPE_VIDEO"/>
    /// </summary>
    [JsonPropertyName("backgroundFileType")]
    public int BackgroundFileType { get; set; }


    /// <summary>
    /// 视频的首帧图，视频还没加载出来时先显示它。
    /// 对应米哈游的 <c>background</c>。
    /// </summary>
    [JsonPropertyName("firstFrameImage")]
    public string? FirstFrameImage { get; set; }


    /// <summary>
    /// 压在背景上的版本标语图，对应米哈游的 <c>theme</c>
    /// </summary>
    [JsonPropertyName("slogan")]
    public string? Slogan { get; set; }


    public const int FUNCTION_ON = 1;

    public const int FILE_TYPE_IMAGE = 1;
    public const int FILE_TYPE_VIDEO = 2;

}
