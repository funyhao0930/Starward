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


    [JsonPropertyName("get_latest_game_req")]
    public GryphlineLatestGameRequest? LatestGameRequest { get; set; }

}


/// <summary>
/// 查询最新游戏包的请求体。
/// <para/>
/// 与背景、公告那一族不同，这支走的是不带 web 的 batch_proxy，
/// 而且渠道必须填对：留空或填错整个 proxy_rsps 都不返回，
/// 不像背景图那样留空也能拿到默认素材。
/// </summary>
public class GryphlineLatestGameRequest
{

    [JsonPropertyName("appcode")]
    public string AppCode { get; set; } = "";


    /// <summary>
    /// 启动器自己的标识，与游戏的 appcode 不是同一个
    /// </summary>
    [JsonPropertyName("launcher_appcode")]
    public string LauncherAppCode { get; set; } = "";


    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "";


    [JsonPropertyName("sub_channel")]
    public string SubChannel { get; set; } = "";


    /// <summary>
    /// 本机版本号。留空时返回整包安装的信息，Starward 读不到本机版本号，因此总是留空。
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

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


    [JsonPropertyName("get_latest_game_rsp")]
    public GryphlineLatestGame? LatestGameResponse { get; set; }

}


/// <summary>
/// 最新游戏包。补丁（patch / pre_patch）这里不建模：它是用 cd_key 加密的 zip，
/// 里面的 VFS 差分是 hpatch 的 HDIFFSF20，Starward 目前按完整文件比对更新，用不到。
/// </summary>
public class GryphlineLatestGame
{

    /// <summary>
    /// 线上的游戏版本号，形如 <c>1.5.3</c>
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }


    /// <summary>
    /// 请求里的版本号是最新版时为 0，留空时为 1（整包安装）
    /// </summary>
    [JsonPropertyName("action")]
    public int Action { get; set; }


    [JsonPropertyName("pkg")]
    public GryphlineGamePackage? Package { get; set; }

}


public class GryphlineGamePackage
{

    /// <summary>
    /// 整包的分卷。是同一个 Zip64 文件按 1 GiB 直接切开，不是多磁盘 zip：
    /// 文件会跨分卷，中央目录在最后一卷的末尾。
    /// 本机已是最新版时为空。
    /// </summary>
    [JsonPropertyName("packs")]
    public List<GryphlinePackageFile>? Packs { get; set; }


    /// <summary>
    /// 逐个文件下载的根目录：<c>{file_path}/{相对路径}</c>，支持 Range。
    /// 本机已是最新版时也会给。
    /// </summary>
    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }


    /// <summary>
    /// 先下载整包再解压需要的最大空间（分卷总和加解压后大小），字符串形式的数字
    /// </summary>
    [JsonPropertyName("total_size")]
    public string? TotalSize { get; set; }


    /// <summary>
    /// 这一版安装清单 <c>game_files</c> 的 MD5。
    /// <para/>
    /// 官方启动器装完会把同一份清单放在游戏目录里，内容逐字节一致，
    /// 所以本机那份的 MD5 与它相等就说明已经是这一版。
    /// 这是判断「要不要更新」唯一可靠的办法：本机的版本文件 config.ini 是加密的。
    /// </summary>
    [JsonPropertyName("game_files_md5")]
    public string? GameFilesMd5 { get; set; }

}


/// <summary>
/// 整包的一个分卷
/// </summary>
public class GryphlinePackageFile
{

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("md5")]
    public string? Md5 { get; set; }

    /// <summary>
    /// 字符串形式的字节数
    /// </summary>
    [JsonPropertyName("package_size")]
    public string? PackageSize { get; set; }

    public long Size => long.TryParse(PackageSize, out long size) ? size : 0;

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
