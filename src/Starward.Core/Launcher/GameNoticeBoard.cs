namespace Starward.Core.Launcher;

/// <summary>
/// 游戏内公告板：米哈游以外的游戏在游戏里弹出的那一块公告，按分页列出。
/// <para/>
/// 米哈游的游戏内公告有现成的网页，直接嵌进 WebView 就好；其他几家都是游戏自己画的界面，
/// 背后只有数据接口，形状也各不相同。各家的 Mapper 把数据换成本类型，
/// 界面只认这一种。
/// </summary>
public class GameNoticeBoard
{

    /// <summary>
    /// 分页，按显示顺序排列，不含空分页
    /// </summary>
    public List<GameNoticeTab> Tabs { get; set; } = [];


    /// <summary>
    /// 版本活动日历，只有鸣潮有，没有时为 null。只有专属的公告窗口画它，通用窗口不理会。
    /// </summary>
    public GameNoticeCalendar? Calendar { get; set; }


    /// <summary>
    /// 公告板上所有会记看过没有的东西：每则公告，加上日历
    /// </summary>
    public IEnumerable<(string Id, bool NeedRedDot)> ReadableEntries =>
        Tabs.SelectMany(x => x.Items).Select(x => (x.Id, x.NeedRedDot))
            .Concat(Calendar is null ? [] : [(Calendar.Id, Calendar.NeedRedDot)]);

}



/// <summary>
/// 版本活动日历：一张长图，图上画好了时间轴与活动卡片，
/// 界面在上面叠一根表示现在的指针，再把每张卡片做成可点的热区。坐标都是相对于整张图的百分比。
/// </summary>
public class GameNoticeCalendar
{

    public string Id { get; set; } = "";


    public bool NeedRedDot { get; set; }


    public string BackgroundUrl { get; set; } = "";


    /// <summary>
    /// 时间轴的起讫时刻
    /// </summary>
    public DateTimeOffset TimelineStart { get; set; }


    public DateTimeOffset TimelineEnd { get; set; }


    /// <summary>
    /// 指针最早停的时刻，现在比它早时停在这里
    /// </summary>
    public DateTimeOffset PointerStart { get; set; }


    /// <summary>
    /// 时间轴起点与终点在图上的横向百分比
    /// </summary>
    public double TimelineX1 { get; set; }


    public double TimelineX2 { get; set; }


    public List<GameNoticeCalendarNode> Nodes { get; set; } = [];


    /// <summary>
    /// 指针在图上的横向百分比：时间轴之前停在起点，之后停在终点，中间按时间线性内插
    /// </summary>
    public double GetPointerX(DateTimeOffset now)
    {
        DateTimeOffset at = now > PointerStart ? now : PointerStart;
        if (TimelineEnd <= TimelineStart || at <= TimelineStart)
        {
            return TimelineX1;
        }
        if (at >= TimelineEnd)
        {
            return TimelineX2;
        }
        return TimelineX1 + (TimelineX2 - TimelineX1) * (at - TimelineStart).TotalMilliseconds / (TimelineEnd - TimelineStart).TotalMilliseconds;
    }

}



/// <summary>
/// 日历图上一块可点的热区
/// </summary>
public class GameNoticeCalendarNode
{

    /// <summary>
    /// 点了要打开的公告
    /// </summary>
    public string NoticeId { get; set; } = "";


    /// <summary>
    /// 那则公告的状态
    /// </summary>
    public GameNoticeCalendarNodeStatus Status { get; set; }


    /// <summary>
    /// 左上与右下角，相对于整张图的百分比
    /// </summary>
    public double X1 { get; set; }


    public double Y1 { get; set; }


    public double X2 { get; set; }


    public double Y2 { get; set; }

}



public enum GameNoticeCalendarNodeStatus
{
    /// <summary>
    /// 已上架，可以打开
    /// </summary>
    Online = 1,

    /// <summary>
    /// 还没上架，提示敬请期待
    /// </summary>
    NotYetOnline = 2,

    /// <summary>
    /// 已下架
    /// </summary>
    Offline = 3,
}



/// <summary>
/// 公告板的一个分页
/// </summary>
public class GameNoticeTab
{

    /// <summary>
    /// 分类，取 <see cref="HoYoPlay.GamePostType"/> 的值，
    /// 与启动页资讯共用一套「公告 / 活动 / 资讯」文案
    /// </summary>
    public string Type { get; set; } = "";


    public List<GameNoticeItem> Items { get; set; } = [];

}



/// <summary>
/// 一则公告
/// </summary>
public class GameNoticeItem
{

    /// <summary>
    /// 在同一款游戏里唯一，用来记住看过没有
    /// </summary>
    public string Id { get; set; } = "";


    /// <summary>
    /// 列表里显示的短标题，可能带换行
    /// </summary>
    public string Title { get; set; } = "";


    /// <summary>
    /// 正文上方的完整标题，没有时用 <see cref="Title"/>
    /// </summary>
    public string? Header { get; set; }


    /// <summary>
    /// 发布日期，<c>MM/dd</c>，没有时为空
    /// </summary>
    public string Date { get; set; } = "";


    /// <summary>
    /// 供应商自己的细分类，没有时为 0。鸣潮的游戏内公告板按它换列表里的图标。
    /// </summary>
    public int Tag { get; set; }


    /// <summary>
    /// 官方标了要提示红点。没有这项标记的接口由 Mapper 按发布时间推断。
    /// </summary>
    public bool NeedRedDot { get; set; }


    /// <summary>
    /// 正文上方的横幅图，没有时为 null
    /// </summary>
    public string? BannerUrl { get; set; }


    /// <summary>
    /// 已经随列表一起拿到的正文 HTML。为 null 时要按 <see cref="ContentUrls"/> 另外去取。
    /// </summary>
    public string? ContentHtml { get; set; }


    /// <summary>
    /// 正文的地址，第一个是主站，其余是备援。内容格式由各家的 Provider 自己认。
    /// </summary>
    public List<string> ContentUrls { get; set; } = [];


    /// <summary>
    /// 正文相对地址的基准，没有时为 null
    /// </summary>
    public string? BaseUrl { get; set; }

}
