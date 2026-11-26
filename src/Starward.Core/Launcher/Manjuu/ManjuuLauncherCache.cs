using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Manjuu;

/// <summary>
/// 官方启动器（Electron）记下的当期首页美术，位于根目录下的 <c>AppData\data\cache.json</c>：
/// <code>
/// {
///   "bgName": "bg_1795526199039.png",
///   "bgUrl": "https://l1-prod-plat-server-rgn1.jimi-global.com/azurpromilia/img/…/…kv.png",
///   "logoName": "logo_1795526199058.png",
///   "logoUrl": "https://…/…logo白.png",
///   "videoName": "video_1795526199078.dat",
///   "videoUrl": "https://l1-prod-plat-server-rgn1.jimi-global.com/azurpromilia/video/…/….mp4"
/// }
/// </code>
/// <c>*Name</c> 是 <c>AppData\MediaCache</c> 里下载好的文件，<c>*Url</c> 是官方 CDN 的公开地址，
/// 取用不需要签名。启动器的首页接口要签名，这份缓存是唯一不必签名的来源。
/// </summary>
public class ManjuuLauncherCache
{

    /// <summary>
    /// 缓存文件相对于官方启动器数据目录的位置
    /// </summary>
    public const string RelativePath = @"data\cache.json";

    /// <summary>
    /// 下载好的美术文件所在目录，相对于官方启动器数据目录
    /// </summary>
    public const string MediaCacheFolderName = "MediaCache";


    [JsonPropertyName("bgName")]
    public string? BackgroundName { get; set; }

    [JsonPropertyName("bgUrl")]
    public string? BackgroundUrl { get; set; }

    [JsonPropertyName("logoName")]
    public string? LogoName { get; set; }

    [JsonPropertyName("logoUrl")]
    public string? LogoUrl { get; set; }

    /// <summary>
    /// 媒体缓存里的视频文件名，扩展名是 .dat，内容是 mp4
    /// </summary>
    [JsonPropertyName("videoName")]
    public string? VideoName { get; set; }

    [JsonPropertyName("videoUrl")]
    public string? VideoUrl { get; set; }


    /// <summary>
    /// 解析 cache.json，内容不对时返回 null
    /// </summary>
    public static ManjuuLauncherCache? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize(json, ManjuuLauncherJsonContext.Default.ManjuuLauncherCache);
        }
        catch (JsonException)
        {
            return null;
        }
    }

}


[JsonSerializable(typeof(ManjuuLauncherCache))]
internal partial class ManjuuLauncherJsonContext : JsonSerializerContext
{

}
