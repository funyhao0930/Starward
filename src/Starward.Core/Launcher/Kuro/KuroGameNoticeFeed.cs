using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 鸣潮 3.8 起游戏内公告板的数据 <c>gamenotice/G153/{serverId}/{lang}.json</c>。
/// <para/>
/// 3.8 的公告板改成游戏 SDK 内嵌的网页（<c>aki-gm-resources-oversea.aki-game.net/aki/announcement/index.html</c>），
/// 网页读的就是这份不需要登录的静态 JSON：按语言一份，四个分页（推荐、公告、资讯、日历）都在里面，
/// 正文也直接内嵌，不必再按 <see cref="KuroGameNotice.ContentPrefix"/> 另取。
/// 旧的 <see cref="KuroGameNoticeList"/>（<c>notice.json</c>）没有推荐与日历。
/// </summary>
public class KuroGameNoticeFeed
{

    /// <summary>
    /// 推荐：唤取横幅与版本重点活动
    /// </summary>
    [JsonPropertyName("recommend")]
    public List<KuroGameNoticeEntry>? Recommend { get; set; }


    /// <summary>
    /// 公告：版本说明、活动说明、营运声明等
    /// </summary>
    [JsonPropertyName("game")]
    public List<KuroGameNoticeEntry>? Game { get; set; }


    /// <summary>
    /// 资讯：社群资讯这类
    /// </summary>
    [JsonPropertyName("activity")]
    public List<KuroGameNoticeEntry>? Activity { get; set; }


    /// <summary>
    /// 版本活动日历，官方网页取第一份符合条件的
    /// </summary>
    [JsonPropertyName("calendar")]
    public List<KuroGameNoticeCalendar>? Calendar { get; set; }

}



/// <summary>
/// 公告与日历共有的上下架条件
/// </summary>
public abstract class KuroGameNoticeFeedBase
{

    [JsonPropertyName("id")]
    [JsonConverter(typeof(KuroStringOrNumberConverter))]
    public string? Id { get; set; }


    /// <summary>
    /// 1 表示要提示红点
    /// </summary>
    [JsonPropertyName("red")]
    public int Red { get; set; }


    [JsonPropertyName("startTimeMs")]
    public long StartTimeMs { get; set; }


    [JsonPropertyName("endTimeMs")]
    public long EndTimeMs { get; set; }


    /// <summary>
    /// 发给哪些平台：1 PC、2 Android、3 iOS、4 PS5、5 Mac、6 XSX、7 鸿蒙、8 WinGDK
    /// </summary>
    [JsonPropertyName("platform")]
    public List<int>? Platform { get; set; }


    /// <summary>
    /// 只发给这些渠道，空表示全部
    /// </summary>
    [JsonPropertyName("channel")]
    public List<JsonElement>? Channel { get; set; }


    /// <summary>
    /// 只发给名单上的账号，多半是测试用，非空时不显示
    /// </summary>
    [JsonPropertyName("whiteList")]
    public List<JsonElement>? WhiteList { get; set; }

}



public class KuroGameNoticeEntry : KuroGameNoticeFeedBase
{

    /// <summary>
    /// 长期公告。官方网页照样看上下架时间，只是不限定登录状态。
    /// </summary>
    [JsonPropertyName("permanent")]
    public int Permanent { get; set; }


    /// <summary>
    /// 标题，已经是这种语言的，可能带换行
    /// </summary>
    [JsonPropertyName("tabTitle")]
    public string? TabTitle { get; set; }


    /// <summary>
    /// 卡片与正文上方的横幅
    /// </summary>
    [JsonPropertyName("tabBanner")]
    public string? TabBanner { get; set; }


    /// <summary>
    /// 小窗口收起时用的窄横幅，没有时为 null
    /// </summary>
    [JsonPropertyName("foldBanner")]
    public string? FoldBanner { get; set; }


    /// <summary>
    /// 正文 HTML。链接写成 <c>browser:https://…</c> 这种带开启方式前缀的形式。
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }


    /// <summary>
    /// 1 公告、2 活动、3 推荐、4 资讯，只作参考，分页按所在的那一组
    /// </summary>
    [JsonPropertyName("category")]
    public int Category { get; set; }


    /// <summary>
    /// 细分类，公告板按它换图标：1 公告、2 维护、3 活动、4 商城、5 限时活动、6 常驻玩法、
    /// 7 唤取、8 新剧情、9 新玩法、10 资讯、11 周边、12 推荐
    /// </summary>
    [JsonPropertyName("tag")]
    public int Tag { get; set; }

}



/// <summary>
/// 版本活动日历：一整张由上往下卷的长图，图上的时间轴与活动卡片都画在图里，
/// 网页只在上面叠一根「现在」指针与可点的热区。坐标都是相对于整张图的百分比。
/// </summary>
public class KuroGameNoticeCalendar : KuroGameNoticeFeedBase
{

    [JsonPropertyName("backgroundUrl")]
    public string? BackgroundUrl { get; set; }


    [JsonPropertyName("timeline")]
    public KuroGameNoticeCalendarTimeline? Timeline { get; set; }


    [JsonPropertyName("nodes")]
    public List<KuroGameNoticeCalendarNode>? Nodes { get; set; }

}



/// <summary>
/// 图上时间轴的范围。日期是 <c>yyyy-MM-dd HH:mm:ss</c>，不带时区，与清单里的毫秒时间对照是 UTC+8。
/// </summary>
public class KuroGameNoticeCalendarTimeline
{

    [JsonPropertyName("startDate")]
    public string? StartDate { get; set; }


    [JsonPropertyName("endDate")]
    public string? EndDate { get; set; }


    /// <summary>
    /// 指针最早停的位置，现在比它早时停在这里
    /// </summary>
    [JsonPropertyName("pointerStartDate")]
    public string? PointerStartDate { get; set; }


    /// <summary>
    /// 时间轴起点在图上的横向百分比
    /// </summary>
    [JsonPropertyName("x1")]
    [JsonConverter(typeof(KuroStringOrNumberConverter))]
    public string? X1 { get; set; }


    /// <summary>
    /// 时间轴终点在图上的横向百分比
    /// </summary>
    [JsonPropertyName("x2")]
    [JsonConverter(typeof(KuroStringOrNumberConverter))]
    public string? X2 { get; set; }

}



/// <summary>
/// 图上一块可点的热区，点了打开对应的公告
/// </summary>
public class KuroGameNoticeCalendarNode
{

    [JsonPropertyName("relatedNoticeId")]
    [JsonConverter(typeof(KuroStringOrNumberConverter))]
    public string? RelatedNoticeId { get; set; }


    /// <summary>
    /// 1 已上架、2 还没上架（点了提示敬请期待）、3 已下架
    /// </summary>
    [JsonPropertyName("relatedNoticeStatus")]
    public int RelatedNoticeStatus { get; set; }


    [JsonPropertyName("x1")]
    [JsonConverter(typeof(KuroStringOrNumberConverter))]
    public string? X1 { get; set; }


    [JsonPropertyName("y1")]
    [JsonConverter(typeof(KuroStringOrNumberConverter))]
    public string? Y1 { get; set; }


    [JsonPropertyName("x2")]
    [JsonConverter(typeof(KuroStringOrNumberConverter))]
    public string? X2 { get; set; }


    [JsonPropertyName("y2")]
    [JsonConverter(typeof(KuroStringOrNumberConverter))]
    public string? Y2 { get; set; }

}
