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
