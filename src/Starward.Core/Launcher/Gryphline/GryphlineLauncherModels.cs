using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Gryphline;

/// <summary>
/// GRYPHLINK 启动器的聚合接口请求体。
/// <para/>
/// 官方启动器一次把首页要的东西全问完（侧栏、横幅、公告、背景图……），
/// 本项目只要背景图，因此只放一个 <c>get_main_bg_image</c>。
/// </summary>
public class GryphlineBatchProxyRequest
{

    [JsonPropertyName("proxy_reqs")]
    public List<GryphlineProxyRequest> ProxyRequests { get; set; } = [];

}


public class GryphlineProxyRequest
{

    /// <summary>
    /// 要问的东西，本项目只用 <see cref="GryphlineLauncherClient.KIND_MAIN_BG_IMAGE"/>。
    /// 接口对不认识的 kind 会返回 INVALID_PARAM。
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";


    [JsonPropertyName("get_main_bg_image_req")]
    public GryphlineMainBgImageRequest? MainBgImageRequest { get; set; }

}


public class GryphlineMainBgImageRequest
{

    /// <summary>
    /// 游戏标识，是一段不透明的令牌而不是名字
    /// </summary>
    [JsonPropertyName("appcode")]
    public string AppCode { get; set; } = "";


    /// <summary>
    /// 完整的地区语言代码，例如 zh-tw
    /// </summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "";


    /// <summary>
    /// 渠道。留空取默认素材，见 <see cref="GryphlineLauncherClient"/> 的说明。
    /// </summary>
    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "";


    [JsonPropertyName("sub_channel")]
    public string SubChannel { get; set; } = "";


    [JsonPropertyName("platform")]
    public string Platform { get; set; } = "Windows";


    [JsonPropertyName("source")]
    public string Source { get; set; } = "launcher";

}


public class GryphlineBatchProxyResponse
{

    [JsonPropertyName("proxy_rsps")]
    public List<GryphlineProxyResponse>? ProxyResponses { get; set; }

}


public class GryphlineProxyResponse
{

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }


    /// <summary>
    /// 问了但没有结果时整个字段都不返回，只留下 kind
    /// </summary>
    [JsonPropertyName("get_main_bg_image_rsp")]
    public GryphlineMainBgImageResponse? MainBgImageResponse { get; set; }

}


public class GryphlineMainBgImageResponse
{

    [JsonPropertyName("main_bg_image")]
    public GryphlineMainBgImage? MainBgImage { get; set; }

}


/// <summary>
/// 首页背景图。没有米哈游那样的版本标语叠图，只有图和视频。
/// </summary>
public class GryphlineMainBgImage
{

    /// <summary>
    /// 静态背景图，也是视频的封面
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }


    /// <summary>
    /// <see cref="Url"/> 的校验值。接口没有背景图 ID，用它判断换没换比文件名更可靠。
    /// </summary>
    [JsonPropertyName("md5")]
    public string? Md5 { get; set; }


    /// <summary>
    /// 动态背景，可以没有
    /// </summary>
    [JsonPropertyName("video_url")]
    public string? VideoUrl { get; set; }

}
