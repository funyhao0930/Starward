using Starward.Core.HoYoPlay;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Starward.Core.Launcher.Hotta;

/// <summary>
/// 把异环官网的横幅与新闻片段换成应用统一使用的 <see cref="GameContent"/>。
/// <para/>
/// 官方外壳首页那一块是内嵌网页 <c>{官网}/platform/index.html</c>，背后没有 JSON 接口：
/// 网页脚本把官网 CMS 生成的 HTML 片段直接塞进页面。横幅来自 <see cref="BannerPath"/>，
/// 四个新闻分页「最新 / 新闻 / 系统 / 活动」依次来自 <c>/news/indexS.html</c>、
/// <c>indexS_1</c>、<c>indexS_2</c>、<c>indexS_3</c>。
/// 片段是 CMS 按模板生成的，结构固定，因此这里按类名切出每一项再取属性。
/// <para/>
/// 这段解析不依赖任何 IO，值得单独测。
/// </summary>
public static class HottaContentMapper
{

    /// <summary>
    /// 横幅片段，相对于官网
    /// </summary>
    public const string BannerPath = "/CmsBanner/ubanner_A.html";


    /// <summary>
    /// 新闻分页的片段与对应的分类，相对于官网。
    /// <para/>
    /// 「最新」那一页是其余三页的合集，不取。
    /// 分类按分页给定，不按每条新闻的角标：官方启动器也是这样分的。
    /// </summary>
    public static IReadOnlyList<(string Path, string PostType)> NewsLists { get; } =
    [
        ("/news/indexS_2.html", GamePostType.POST_TYPE_ANNOUNCE),
        ("/news/indexS_3.html", GamePostType.POST_TYPE_ACTIVITY),
        ("/news/indexS_1.html", GamePostType.POST_TYPE_INFO),
    ];


    /// <summary>
    /// 横幅的上下架时间是台湾时间，写法是 <c>2026/09/30 10:00:00</c>，不带时区。
    /// 官方网页交给浏览器按本机时区解读，只有人在 UTC+8 时才对得上，这里直接按 UTC+8 算。
    /// </summary>
    private static readonly TimeSpan PublishTimeOffset = TimeSpan.FromHours(8);


    /// <summary>
    /// 一张横幅的开始标签，上下架时间就写在它上面
    /// </summary>
    private static readonly Regex BannerStartRegex = new(ClassTagPattern("div", "carousel-item"), RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 一条新闻的开始标签。片段前半是给轮播用的缩略图列表，同样的新闻又列了一次，
    /// 但那一份没有日期，只认这一份。
    /// </summary>
    private static readonly Regex NewsStartRegex = new(ClassTagPattern("div", "intelSlideCont"), RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NewsTitleRegex = new(ClassTagPattern("a", "intelSlideTit"), RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NewsDateRegex = new(ClassTagPattern("div", "intelDate") + @"\s*([^<]*)<", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LinkTagRegex = new(@"<a\s[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SourceTagRegex = new(@"<source\s[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ImageTagRegex = new(@"<img\s[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);


    /// <summary>
    /// 解析横幅片段，按片段里的顺序返回，不筛上下架时间
    /// </summary>
    /// <param name="baseUri">片段本身的地址，片段里的图片与链接相对于它</param>
    public static List<HottaBanner> ParseBanners(string? html, Uri baseUri)
    {
        var banners = new List<HottaBanner>();
        foreach ((string start, string body) in SplitItems(html, BannerStartRegex))
        {
            // 第一个 source 是给桌面宽度的，img 是认不得 source 时的后备
            string? image = GetAttribute(SourceTagRegex.Match(body), "srcset") ?? GetAttribute(ImageTagRegex.Match(body), "src");
            if (ToAbsoluteUrl(baseUri, image) is not string url)
            {
                continue;
            }
            banners.Add(new HottaBanner
            {
                ImageUrl = url,
                Link = ToAbsoluteUrl(baseUri, GetAttribute(LinkTagRegex.Match(body), "href")) ?? "",
                OnTime = ParsePublishTime(GetAttribute(start, "on")),
                OffTime = ParsePublishTime(GetAttribute(start, "off")),
            });
        }
        return banners;
    }


    /// <summary>
    /// 解析一个新闻分页片段，按片段里的顺序返回。
    /// <para/>
    /// 顺序不是按日期排的，置顶的在前，与官方启动器一致，不重排。
    /// </summary>
    /// <param name="baseUri">片段本身的地址，片段里的链接相对于它</param>
    public static List<HottaNewsItem> ParseNews(string? html, Uri baseUri)
    {
        var items = new List<HottaNewsItem>();
        foreach ((_, string body) in SplitItems(html, NewsStartRegex))
        {
            Match title = NewsTitleRegex.Match(body);
            if (!title.Success)
            {
                continue;
            }
            // title 属性就是完整的标题；标签里的文字可能夹着别的标签，只当后备
            string text = WebUtility.HtmlDecode(GetAttribute(title.Value, "title") ?? GetInnerText(body, title)).Trim();
            if (string.IsNullOrWhiteSpace(text) || ToAbsoluteUrl(baseUri, GetAttribute(title.Value, "href")) is not string link)
            {
                continue;
            }
            Match date = NewsDateRegex.Match(body);
            items.Add(new HottaNewsItem
            {
                Title = text,
                Link = link,
                Date = date.Success ? date.Groups[1].Value.Trim() : "",
            });
        }
        return items;
    }


    /// <summary>
    /// 换成统一的横幅与资讯，没有可用内容时返回 null
    /// </summary>
    /// <param name="newsLists">各个分页解析出来的新闻，按 <see cref="NewsLists"/> 的顺序</param>
    /// <param name="now">筛横幅上下架时间用的当前时刻</param>
    public static GameContent? ToGameContent(IEnumerable<HottaBanner>? banners,
                                             IEnumerable<(string PostType, List<HottaNewsItem> Items)>? newsLists,
                                             DateTimeOffset now)
    {
        var gameBanners = new List<GameBanner>();
        foreach (HottaBanner banner in banners ?? [])
        {
            // 片段里还留着已经下架、尚未上架的横幅，官方网页是在浏览器里按时间筛掉的
            if (banner.OnTime > now || banner.OffTime < now)
            {
                continue;
            }
            gameBanners.Add(new GameBanner
            {
                // 片段里没有横幅 ID，图片文件名随横幅一起换，拿地址当唯一标识
                Id = banner.ImageUrl,
                Image = new GameImage
                {
                    Url = banner.ImageUrl,
                    Link = banner.Link,
                },
            });
        }

        var posts = new List<GamePost>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string type, List<HottaNewsItem> items) in newsLists ?? [])
        {
            foreach (HottaNewsItem item in items)
            {
                // 同一篇新闻可能同时列在两个分页，只留第一次出现的
                if (!seen.Add(item.Link))
                {
                    continue;
                }
                posts.Add(new GamePost
                {
                    // 片段里没有新闻 ID，文章地址本身就是唯一的
                    Id = item.Link,
                    Type = type,
                    Title = item.Title,
                    Link = item.Link,
                    Date = FormatDate(item.Date),
                });
            }
        }

        if (gameBanners.Count is 0 && posts.Count is 0)
        {
            return null;
        }
        return new GameContent
        {
            Banners = gameBanners,
            Posts = posts,
            SocialMediaList = [],
        };
    }


    /// <summary>
    /// 某个类名的开始标签。类名两边不能再接字母或连字号，
    /// 否则 <c>carousel-item</c> 也会认到 <c>carousel-item-next</c> 这类状态类。
    /// </summary>
    private static string ClassTagPattern(string tagName, string className)
    {
        return $@"<{tagName}\s[^>]*\bclass\s*=\s*""[^""]*(?<![\w-]){Regex.Escape(className)}(?![\w-])[^""]*""[^>]*>";
    }


    /// <summary>
    /// 按每一项的开始标签切开：每一项从自己的开始标签到下一项的开始标签为止。
    /// <para/>
    /// 不靠配对结束标签：项目里有没有嵌套的 div 由模板决定，按开始标签切不受影响。
    /// </summary>
    private static IEnumerable<(string Start, string Body)> SplitItems(string? html, Regex startRegex)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            yield break;
        }
        MatchCollection starts = startRegex.Matches(html);
        for (int i = 0; i < starts.Count; i++)
        {
            int end = i + 1 < starts.Count ? starts[i + 1].Index : html.Length;
            yield return (starts[i].Value, html[starts[i].Index..end]);
        }
    }


    private static string? GetAttribute(Match tag, string name)
    {
        return tag.Success ? GetAttribute(tag.Value, name) : null;
    }


    /// <summary>
    /// 标签里某个属性的值，没有返回 null
    /// </summary>
    private static string? GetAttribute(string tag, string name)
    {
        Match match = Regex.Match(tag, $@"\s{Regex.Escape(name)}\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }


    /// <summary>
    /// 开始标签之后到 <c>&lt;/a&gt;</c> 之间的文字，去掉夹在里面的标签
    /// </summary>
    private static string GetInnerText(string body, Match startTag)
    {
        int start = startTag.Index + startTag.Length;
        int end = body.IndexOf("</a>", start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? "" : Regex.Replace(body[start..end], "<[^>]*>", "");
    }


    /// <summary>
    /// 片段里的地址多半是站内的相对路径，按片段的地址补成完整地址；空的或不是网页地址的返回 null
    /// </summary>
    private static string? ToAbsoluteUrl(Uri baseUri, string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }
        return Uri.TryCreate(baseUri, WebUtility.HtmlDecode(url.Trim()), out Uri? uri) && uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;
    }


    /// <summary>
    /// 上下架时间认不出来就当作没有限制：宁可多显示一张横幅，也不要整块空掉
    /// </summary>
    private static DateTimeOffset? ParsePublishTime(string? text)
    {
        if (DateTime.TryParseExact(text?.Trim(), "yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time))
        {
            return new DateTimeOffset(time, PublishTimeOffset);
        }
        return null;
    }


    /// <summary>
    /// 片段给的是 <c>yyyy-MM-dd</c>，换成与米哈游一致的 <c>MM/dd</c>。
    /// 认不出来的写法原样返回，总比显示空白好。
    /// </summary>
    private static string FormatDate(string? date)
    {
        if (DateTime.TryParseExact(date?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time))
        {
            return time.ToString("MM/dd", CultureInfo.InvariantCulture);
        }
        return date?.Trim() ?? "";
    }

}



/// <summary>
/// 官网横幅片段里的一张横幅
/// </summary>
public class HottaBanner
{

    public string ImageUrl { get; set; } = "";

    /// <summary>
    /// 点击横幅打开的地址，可能是站外的影片
    /// </summary>
    public string Link { get; set; } = "";

    /// <summary>
    /// 上架时间，没有写或认不出来时为 null
    /// </summary>
    public DateTimeOffset? OnTime { get; set; }

    /// <summary>
    /// 下架时间，没有写或认不出来时为 null。长期挂着的横幅写的是 2999 年。
    /// </summary>
    public DateTimeOffset? OffTime { get; set; }

}



/// <summary>
/// 官网新闻分页片段里的一条新闻
/// </summary>
public class HottaNewsItem
{

    public string Title { get; set; } = "";

    public string Link { get; set; } = "";

    /// <summary>
    /// 发布日期，<c>yyyy-MM-dd</c>
    /// </summary>
    public string Date { get; set; } = "";

}
