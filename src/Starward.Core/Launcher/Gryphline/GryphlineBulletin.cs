using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Gryphline;

/// <summary>
/// 终末地游戏内公告的聚合接口 <c>game-hub.gryphline.com/bulletin/v2/aggregate</c> 的返回。
/// <para/>
/// 游戏内的公告窗口是 webviewsdk 打开的内置网页，网页地址写在加密的 <c>BulletinConfig.bin</c> 里，
/// 但数据来自这支不需要登录的接口，<c>hideDetail=0</c> 时连正文一起返回。
/// 参数取自 GitHub 上 daydreamer-json/ak-endfield-api-archive 记下的写法。
/// </summary>
public class GryphlineBulletinResponse
{

    /// <summary>
    /// 0 表示成功
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }


    [JsonPropertyName("msg")]
    public string? Message { get; set; }


    [JsonPropertyName("data")]
    public GryphlineBulletinData? Data { get; set; }

}



public class GryphlineBulletinData
{

    /// <summary>
    /// 每则公告的版本与红点标记，按 <see cref="GryphlineBulletin.Cid"/> 对应
    /// </summary>
    [JsonPropertyName("onlineList")]
    public List<GryphlineBulletinOnline>? OnlineList { get; set; }


    [JsonPropertyName("list")]
    public List<GryphlineBulletin>? List { get; set; }

}



public class GryphlineBulletinOnline
{

    [JsonPropertyName("cid")]
    public string? Cid { get; set; }


    [JsonPropertyName("needRedDot")]
    public bool NeedRedDot { get; set; }

}



public class GryphlineBulletin
{

    [JsonPropertyName("cid")]
    public string? Cid { get; set; }


    /// <summary>
    /// 分页：<see cref="TAB_UPDATES"/>、<see cref="TAB_EVENTS"/>、<see cref="TAB_NEWS"/>
    /// </summary>
    [JsonPropertyName("tab")]
    public string? Tab { get; set; }

    public const string TAB_UPDATES = "updates";

    public const string TAB_EVENTS = "events";

    public const string TAB_NEWS = "news";


    /// <summary>
    /// <see cref="DISPLAY_RICH_TEXT"/> 时 <see cref="Data"/> 是正文，
    /// <see cref="DISPLAY_PICTURE"/> 时是一张图和点图打开的链接
    /// </summary>
    [JsonPropertyName("displayType")]
    public string? DisplayType { get; set; }

    public const string DISPLAY_RICH_TEXT = "rich_text";

    public const string DISPLAY_PICTURE = "picture";


    /// <summary>
    /// 上架时间，Unix 秒
    /// </summary>
    [JsonPropertyName("startAt")]
    public long StartAt { get; set; }


    /// <summary>
    /// 列表里的短标题，可能带换行
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }


    /// <summary>
    /// 正文上方的完整标题，图片类公告是空的
    /// </summary>
    [JsonPropertyName("header")]
    public string? Header { get; set; }


    /// <summary>
    /// 形状随 <see cref="DisplayType"/> 变，先留着原样，由 Mapper 按类型取
    /// </summary>
    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }

}
