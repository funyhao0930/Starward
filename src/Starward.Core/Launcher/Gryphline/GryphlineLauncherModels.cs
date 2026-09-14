using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Gryphline;

/// <summary>
/// GRYPHLINK 启动器的聚合接口请求体。
/// <para/>
/// 官方启动器一次把首页要的东西全问完（侧栏、横幅、公告、背景图……），
/// 本项目要其中的背景图、横幅与公告。
/// </summary>
public class GryphlineBatchProxyRequest
{

    [JsonPropertyName("proxy_reqs")]
    public List<GryphlineProxyRequest> ProxyRequests { get; set; } = [];

}


public class GryphlineProxyRequest
{

    /// <summary>
    /// 要问的东西，见 <see cref="GryphlineLauncherClient"/> 中的 KIND_ 常量。
    /// 接口对不认识的 kind 会返回 INVALID_PARAM。
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";


    [JsonPropertyName("get_main_bg_image_req")]
    public GryphlineLauncherRequest? MainBgImageRequest { get; set; }


    [JsonPropertyName("get_banner_req")]
    public GryphlineLauncherRequest? BannerRequest { get; set; }


    [JsonPropertyName("get_announcement_req")]
    public GryphlineLauncherRequest? AnnouncementRequest { get; set; }

}


/// <summary>
/// 每个 kind 的请求体形状都一样，共用一个类型
/// </summary>
public class GryphlineLauncherRequest
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


    [JsonPropertyName("get_banner_rsp")]
    public GryphlineBannerResponse? BannerResponse { get; set; }


    [JsonPropertyName("get_announcement_rsp")]
    public GryphlineAnnouncementResponse? AnnouncementResponse { get; set; }

}


public class GryphlineBannerResponse
{

    [JsonPropertyName("banners")]
    public List<GryphlineBanner>? Banners { get; set; }

}


/// <summary>
/// 首页轮播图
/// </summary>
public class GryphlineBanner
{

    [JsonPropertyName("id")]
    public string? Id { get; set; }


    [JsonPropertyName("url")]
    public string? Url { get; set; }


    /// <summary>
    /// 点击后打开的链接
    /// </summary>
    [JsonPropertyName("jump_url")]
    public string? JumpUrl { get; set; }


    [JsonPropertyName("md5")]
    public string? Md5 { get; set; }


    /// <summary>
    /// 链接要带登录令牌才能打开。Starward 不登录鹰角账号，
    /// 这类链接照常打开，由浏览器去处理登录。
    /// </summary>
    [JsonPropertyName("need_token")]
    public bool NeedToken { get; set; }

}


public class GryphlineAnnouncementResponse
{

    /// <summary>
    /// 分页。名称已本地化，分类只能按顺序判断，
    /// 见 <see cref="GryphlineContentMapper"/> 的说明。
    /// </summary>
    [JsonPropertyName("tabs")]
    public List<GryphlineAnnouncementTab>? Tabs { get; set; }

}


public class GryphlineAnnouncementTab
{

    [JsonPropertyName("tabName")]
    public string? TabName { get; set; }


    /// <summary>
    /// 分页标识。注意它<b>随语言变化</b>（繁中是 60/61/62，英文是 48/49/50），
    /// 因此不能拿来当分类依据。
    /// </summary>
    [JsonPropertyName("tab_id")]
    public string? TabId { get; set; }


    [JsonPropertyName("announcements")]
    public List<GryphlineAnnouncement>? Announcements { get; set; }

}


public class GryphlineAnnouncement
{

    [JsonPropertyName("id")]
    public string? Id { get; set; }


    /// <summary>
    /// 标题
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }


    [JsonPropertyName("jump_url")]
    public string? JumpUrl { get; set; }


    /// <summary>
    /// 发布时间，Unix 毫秒的字符串
    /// </summary>
    [JsonPropertyName("start_ts")]
    public string? StartTimestamp { get; set; }


    /// <summary>
    /// 置顶，非 0 时排在本组最前
    /// </summary>
    [JsonPropertyName("pin")]
    public int Pin { get; set; }


    [JsonPropertyName("need_token")]
    public bool NeedToken { get; set; }

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
