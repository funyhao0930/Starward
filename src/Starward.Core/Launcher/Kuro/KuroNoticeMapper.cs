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
    /// 换成公告板，没有可显示的公告时返回 null。
    /// <para/>
    /// 分页与游戏内一致：「公告」是 <see cref="KuroGameNoticeList.Game"/> 整组（活动说明也在里面），
    /// 「资讯」是 <see cref="KuroGameNoticeList.Activity"/>；各自按清单的顺序。
    /// 游戏里还有一页「推荐」，放的是唤取横幅，那份数据在游戏本体里，这里没有。
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
