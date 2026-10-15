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
/// 最新游戏包，以及从请求里那个版本过来的补丁与预下载
/// </summary>
public class GryphlineLatestGame
{

    /// <summary>
    /// 线上的游戏版本号，形如 <c>1.5.3</c>
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }


    /// <summary>
    /// 请求里的版本号是最新版时为 0，留空或落后时为 1
    /// </summary>
    [JsonPropertyName("action")]
    public int Action { get; set; }


    /// <summary>
    /// 请求里带的本机版本号，原样返回
    /// </summary>
    [JsonPropertyName("request_version")]
    public string? RequestVersion { get; set; }


    [JsonPropertyName("pkg")]
    public GryphlineGamePackage? Package { get; set; }


    /// <summary>
    /// 从请求的版本更新到 <see cref="Version"/> 的补丁。请求里的版本号留空、或那个版本没有补丁时为 null
    /// </summary>
    [JsonPropertyName("patch")]
    public GryphlinePatch? Patch { get; set; }


    /// <summary>
    /// 预下载：请求的版本已是最新、下一版的补丁已经放出时才有。
    /// 只有分卷，没有 <see cref="GryphlinePatch.CdKey"/> 与 patch.json：
    /// 分卷与正式上线后 <see cref="Patch"/> 的分卷是同一批文件（MD5 相同），上线后才能解密。
    /// </summary>
    [JsonPropertyName("pre_patch")]
    public GryphlinePatch? PrePatch { get; set; }

}


/// <summary>
/// 补丁。是一个按 1 GiB 切开的 zip，WinZip AES-256 加密，密码是 <see cref="CdKey"/>。
/// <para/>
/// zip 里：变了的非 VFS 文件按游戏目录里的相对路径放；新的 VFS 文件在 <c>vfs_files/files/</c> 下；
/// VFS 的差分在 <c>vfs_files/vfs_patch/</c> 下（HDIFFSF20）；另有 patch.json、verify_files.json、
/// delete_files.txt 与新版的 game_files、config.ini。
/// </summary>
public class GryphlinePatch
{

    /// <summary>
    /// 补丁的目标版本，只有预下载会给
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }


    /// <summary>
    /// 分卷，与 <see cref="GryphlineGamePackage.Packs"/> 一样是同一个 zip 直接切开
    /// </summary>
    [JsonPropertyName("patches")]
    public List<GryphlinePackageFile>? Patches { get; set; }


    /// <summary>
    /// 分卷总大小，字符串形式的数字
    /// </summary>
    [JsonPropertyName("package_size")]
    public string? PackageSize { get; set; }

    public long Size => long.TryParse(PackageSize, out long size) ? size : Patches?.Sum(x => x.Size) ?? 0;


    /// <summary>
    /// zip 的密码，正式上线后才有
    /// </summary>
    [JsonPropertyName("cd_key")]
    public string? CdKey { get; set; }


    /// <summary>
    /// 明文的 patch.json，与 zip 里那份相同，可以先拿来确认本机是不是补丁的起点版本
    /// </summary>
    [JsonPropertyName("v2_patch_info_url")]
    public string? PatchInfoUrl { get; set; }


    [JsonPropertyName("v2_patch_info_md5")]
    public string? PatchInfoMd5 { get; set; }

}


/// <summary>
/// 补丁里的 patch.json：新版本 VFS 目录里与这次更新有关的全部文件
/// </summary>
public class GryphlinePatchInfo
{

    [JsonPropertyName("version")]
    public string? Version { get; set; }


    /// <summary>
    /// VFS 目录相对游戏目录的路径，<c>Endfield_Data/StreamingAssets/VFS</c>
    /// </summary>
    [JsonPropertyName("vfs_base_path")]
    public string? VfsBasePath { get; set; }


    [JsonPropertyName("files")]
    public List<GryphlinePatchFile>? Files { get; set; }

}


/// <summary>
/// patch.json 里的一个文件。三种情况：
/// 有 <see cref="Patch"/> 的由旧文件打差分得到；有 <see cref="LocalPath"/> 的是 zip 里的新文件；
/// 都没有的是没变的文件，本机应该已经有了。
/// </summary>
public class GryphlinePatchFile
{

    /// <summary>
    /// 相对 VFS 目录的路径
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";


    [JsonPropertyName("md5")]
    public string? Md5 { get; set; }


    [JsonPropertyName("size")]
    public long Size { get; set; }


    [JsonPropertyName("patch")]
    public List<GryphlinePatchDiff>? Patch { get; set; }


    /// <summary>
    /// 新文件在 zip 里的路径
    /// </summary>
    [JsonPropertyName("local_path")]
    public string? LocalPath { get; set; }

}


/// <summary>
/// 一个差分
/// </summary>
public class GryphlinePatchDiff
{

    /// <summary>
    /// 旧文件，相对 VFS 目录。一个旧文件可能是好几个新文件的起点
    /// </summary>
    [JsonPropertyName("base_file")]
    public string BaseFile { get; set; } = "";


    [JsonPropertyName("base_md5")]
    public string? BaseMd5 { get; set; }


    [JsonPropertyName("base_size")]
    public long BaseSize { get; set; }


    /// <summary>
    /// 差分在 zip 里 <c>vfs_files/vfs_patch/</c> 下的路径
    /// </summary>
    [JsonPropertyName("patch")]
    public string Patch { get; set; } = "";


    [JsonPropertyName("patch_size")]
    public long PatchSize { get; set; }

}


/// <summary>
/// 补丁里的 verify_files.json：官方更新完要校验的文件
/// </summary>
public class GryphlineVerifyFiles
{

    /// <summary>
    /// 从 zip 原样放进游戏目录的文件（含新的 VFS 文件、game_files 与 config.ini）
    /// </summary>
    [JsonPropertyName("move")]
    public List<GryphlineVerifyFile>? Move { get; set; }


    /// <summary>
    /// 打差分得到的文件
    /// </summary>
    [JsonPropertyName("patch")]
    public List<GryphlineVerifyFile>? Patch { get; set; }

}


public class GryphlineVerifyFile
{

    /// <summary>
    /// 相对游戏目录的路径
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";


    [JsonPropertyName("md5")]
    public string? Md5 { get; set; }


    [JsonPropertyName("size")]
    public long Size { get; set; }

}


/// <summary>
/// 预下载完成的标记，放在暂存目录里
/// </summary>
public class GryphlinePredownloadMarker
{

    [JsonPropertyName("local_version")]
    public string LocalVersion { get; set; } = "";


    [JsonPropertyName("target_version")]
    public string TargetVersion { get; set; } = "";

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
