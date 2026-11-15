using Starward.Core.HoYoPlay;
using System.Globalization;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 把鸣潮的游戏内公告清单换成统一的 <see cref="GameNoticeBoard"/>。
/// <para/>
/// 这段转换不依赖任何 IO，值得单独测。
/// </summary>
public static class KuroNoticeMapper
{

    /// <summary>
    /// 清单与正文缺这种语言时退回的语言
    /// </summary>
    private const string FallbackLanguage = "en";


    /// <summary>
    /// 「推荐」分页的 <see cref="GameNoticeTab.Type"/>。米哈游那套分类里没有这一种，鸣潮专用。
    /// </summary>
    public const string RecommendTabType = "POST_TYPE_RECOMMEND";


    /// <summary>
    /// 官方网页给 PC 版的平台代码
    /// </summary>
    private const int PlatformPC = 1;


    /// <summary>
    /// 日历时间轴上的日期不带时区，与清单里的毫秒时间对照是 UTC+8
    /// </summary>
    private static readonly TimeSpan CalendarOffset = TimeSpan.FromHours(8);


    /// <summary>
    /// 把 3.8 起的 <see cref="KuroGameNoticeFeed"/> 换成公告板，没有可显示的内容时返回 null。
    /// <para/>
    /// 分页与游戏内一致：推荐、公告、资讯，各按清单的顺序；日历另放 <see cref="GameNoticeBoard.Calendar"/>。
    /// 上下架条件照官方网页：在上下架时间之间、发给 PC、没有白名单、不限渠道。
    /// </summary>
    /// <param name="now">筛上下架时间用的当前时刻</param>
    public static GameNoticeBoard? ToNoticeBoard(KuroGameNoticeFeed? feed, DateTimeOffset now)
    {
        if (feed is null)
        {
            return null;
        }
        var board = new GameNoticeBoard();
        AddTab(board, RecommendTabType, feed.Recommend, now);
        AddTab(board, GamePostType.POST_TYPE_ANNOUNCE, feed.Game, now);
        AddTab(board, GamePostType.POST_TYPE_INFO, feed.Activity, now);
        board.Calendar = feed.Calendar?.Where(x => IsOnline(x, now)).Select(ToCalendar).FirstOrDefault(x => x is not null);
        return board.Tabs.Count > 0 || board.Calendar is not null ? board : null;
    }


    private static void AddTab(GameNoticeBoard board, string type, List<KuroGameNoticeEntry>? entries, DateTimeOffset now)
    {
        var items = new List<GameNoticeItem>();
        foreach (KuroGameNoticeEntry entry in entries ?? [])
        {
            if (!IsOnline(entry, now) || string.IsNullOrWhiteSpace(entry.TabTitle))
            {
                continue;
            }
            items.Add(new GameNoticeItem
            {
                Id = entry.Id!,
                Title = entry.TabTitle.Trim(),
                Date = entry.StartTimeMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(entry.StartTimeMs).ToLocalTime().ToString("MM/dd", CultureInfo.InvariantCulture) : "",
                Tag = entry.Tag,
                // 官方网页里长期公告从不亮红点，也就不会被记成看过；这里照样不算，免得启动页的红点永远消不掉
                NeedRedDot = entry.Red is 1 && entry.Permanent is 0,
                BannerUrl = string.IsNullOrWhiteSpace(entry.TabBanner) ? null : entry.TabBanner,
                // 没有正文时给空字符串而不是 null，免得被当成还要另外去取
                ContentHtml = entry.Content ?? "",
            });
        }
        if (items.Count > 0)
        {
            board.Tabs.Add(new GameNoticeTab { Type = type, Items = items });
        }
    }


    /// <summary>
    /// 官方网页的筛选条件。没登录时白名单非空的一律不显示；桌面版官方渠道看不到指定渠道的。
    /// </summary>
    private static bool IsOnline(KuroGameNoticeFeedBase entry, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            return false;
        }
        long nowMs = now.ToUnixTimeMilliseconds();
        if (entry.StartTimeMs >= nowMs || entry.EndTimeMs <= nowMs)
        {
            return false;
        }
        if (entry.WhiteList?.Count > 0 || entry.Channel?.Count > 0)
        {
            return false;
        }
        return entry.Platform?.Contains(PlatformPC) ?? false;
    }


    private static GameNoticeCalendar? ToCalendar(KuroGameNoticeCalendar calendar)
    {
        KuroGameNoticeCalendarTimeline? timeline = calendar.Timeline;
        if (string.IsNullOrWhiteSpace(calendar.BackgroundUrl) || timeline is null
            || !TryParseCalendarDate(timeline.StartDate, out DateTimeOffset start)
            || !TryParseCalendarDate(timeline.EndDate, out DateTimeOffset end)
            || !TryParsePercent(timeline.X1, out double x1)
            || !TryParsePercent(timeline.X2, out double x2))
        {
            return null;
        }
        var result = new GameNoticeCalendar
        {
            Id = calendar.Id!,
            NeedRedDot = calendar.Red is 1,
            BackgroundUrl = calendar.BackgroundUrl,
            TimelineStart = start,
            TimelineEnd = end,
            PointerStart = TryParseCalendarDate(timeline.PointerStartDate, out DateTimeOffset pointer) ? pointer : start,
            TimelineX1 = x1,
            TimelineX2 = x2,
        };
        foreach (KuroGameNoticeCalendarNode node in calendar.Nodes ?? [])
        {
            if (!string.IsNullOrWhiteSpace(node.RelatedNoticeId)
                && TryParsePercent(node.X1, out double nx1) && TryParsePercent(node.Y1, out double ny1)
                && TryParsePercent(node.X2, out double nx2) && TryParsePercent(node.Y2, out double ny2)
                && nx2 > nx1 && ny2 > ny1)
            {
                result.Nodes.Add(new GameNoticeCalendarNode
                {
                    NoticeId = node.RelatedNoticeId,
                    Status = (GameNoticeCalendarNodeStatus)node.RelatedNoticeStatus,
                    X1 = nx1,
                    Y1 = ny1,
                    X2 = nx2,
                    Y2 = ny2,
                });
            }
        }
        return result;
    }


    private static bool TryParseCalendarDate(string? value, out DateTimeOffset result)
    {
        if (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
        {
            result = new DateTimeOffset(date, CalendarOffset);
            return true;
        }
        result = default;
        return false;
    }


    private static bool TryParsePercent(string? value, out double result)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);
    }


    /// <summary>
    /// 换成公告板，没有可显示的公告时返回 null。3.8 以前的旧清单，新数据取不到时兜底。
    /// <para/>
    /// 分页与游戏内一致：「公告」是 <see cref="KuroGameNoticeList.Game"/> 整组（活动说明也在里面），
    /// 「资讯」是 <see cref="KuroGameNoticeList.Activity"/>；各自按清单的顺序。
    /// 这份旧清单没有「推荐」与「日历」，那两页只在 <see cref="KuroGameNoticeFeed"/> 里。
    /// </summary>
    /// <param name="language">官方启动器的语言代码，见 <see cref="KuroLauncherClient.GetLanguageCode"/></param>
    /// <param name="now">筛上下架时间用的当前时刻</param>
    public static GameNoticeBoard? ToNoticeBoard(KuroGameNoticeList? list, string language, DateTimeOffset now)
    {
        if (list is null)
        {
            return null;
        }
        var board = new GameNoticeBoard();
        AddTab(board, GamePostType.POST_TYPE_ANNOUNCE, list.Game, language, now);
        AddTab(board, GamePostType.POST_TYPE_INFO, list.Activity, language, now);
        return board.Tabs.Count > 0 ? board : null;
    }


    private static void AddTab(GameNoticeBoard board, string type, List<KuroGameNotice>? notices, string language, DateTimeOffset now)
    {
        var items = new List<GameNoticeItem>();
        foreach (KuroGameNotice notice in notices ?? [])
        {
            if (ToNoticeItem(notice, language, now) is GameNoticeItem item)
            {
                items.Add(item);
            }
        }
        if (items.Count > 0)
        {
            board.Tabs.Add(new GameNoticeTab { Type = type, Items = items });
        }
    }


    /// <summary>
    /// 一则公告，不该显示的返回 null
    /// </summary>
    private static GameNoticeItem? ToNoticeItem(KuroGameNotice notice, string language, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(notice.Id) || notice.ContentPrefix is not { Count: > 0 })
        {
            return null;
        }
        // 白名单是给测试账号看的；指定渠道的是别的渠道包（如手机商店）专属，桌面版官方渠道看不到
        if (notice.WhiteList?.Count > 0 || notice.Channel?.Count > 0)
        {
            return null;
        }
        if (notice.Permanent is 0)
        {
            long nowMs = now.ToUnixTimeMilliseconds();
            if ((notice.StartTimeMs > 0 && notice.StartTimeMs > nowMs) || (notice.EndTimeMs > 0 && notice.EndTimeMs < nowMs))
            {
                return null;
            }
        }
        string? title = GetLocalized(notice.TabTitle, language);
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }
        return new GameNoticeItem
        {
            Id = notice.Id,
            Title = title.Trim(),
            Date = notice.StartTimeMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(notice.StartTimeMs).ToLocalTime().ToString("MM/dd", CultureInfo.InvariantCulture) : "",
            Tag = notice.Tag,
            NeedRedDot = notice.Red is 1,
            BannerUrl = GetLocalized(notice.TabBanner, language)?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
            ContentUrls = notice.ContentPrefix.Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
        };
    }


    private static T? GetLocalized<T>(Dictionary<string, T>? values, string language) where T : class
    {
        if (values is null || values.Count is 0)
        {
            return null;
        }
        if (values.TryGetValue(language, out T? value) || values.TryGetValue(FallbackLanguage, out value))
        {
            return value;
        }
        return values.Values.First();
    }


    /// <summary>
    /// 正文换成可以直接显示的 HTML，没有正文时返回 null
    /// </summary>
    public static string? ToContentHtml(KuroGameNoticeContent? content)
    {
        return string.IsNullOrWhiteSpace(content?.TextContent) ? null : content.TextContent;
    }

}
