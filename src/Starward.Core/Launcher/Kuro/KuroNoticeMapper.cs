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
    /// 分页顺序，与游戏内一致：公告在前
    /// </summary>
    private static readonly string[] TabOrder = [GamePostType.POST_TYPE_ANNOUNCE, GamePostType.POST_TYPE_ACTIVITY, GamePostType.POST_TYPE_INFO];


    /// <summary>
    /// 换成公告板，没有可显示的公告时返回 null
    /// </summary>
    /// <param name="language">官方启动器的语言代码，见 <see cref="KuroLauncherClient.GetLanguageCode"/></param>
    /// <param name="now">筛上下架时间用的当前时刻</param>
    public static GameNoticeBoard? ToNoticeBoard(KuroGameNoticeList? list, string language, DateTimeOffset now)
    {
        if (list is null)
        {
            return null;
        }
        var items = new List<(string Type, GameNoticeItem Item)>();
        // 两组里各自按清单的顺序，那就是游戏内的顺序
        foreach (KuroGameNotice notice in (list.Game ?? []).Concat(list.Activity ?? []))
        {
            if (ToNoticeItem(notice, language, now) is GameNoticeItem item)
            {
                items.Add((GetPostType(notice, list), item));
            }
        }
        var board = new GameNoticeBoard();
        foreach (string type in TabOrder)
        {
            List<GameNoticeItem> tabItems = items.Where(x => x.Type == type).Select(x => x.Item).ToList();
            if (tabItems.Count > 0)
            {
                board.Tabs.Add(new GameNoticeTab { Type = type, Items = tabItems });
            }
        }
        return board.Tabs.Count > 0 ? board : null;
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
            NeedRedDot = notice.Red is 1,
            BannerUrl = GetLocalized(notice.TabBanner, language)?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
            ContentUrls = notice.ContentPrefix.Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
        };
    }


    /// <summary>
    /// 游戏内按 <see cref="KuroGameNotice.Category"/> 分页；认不出来的按它所在的那一组
    /// </summary>
    private static string GetPostType(KuroGameNotice notice, KuroGameNoticeList list)
    {
        return notice.Category switch
        {
            1 => GamePostType.POST_TYPE_ANNOUNCE,
            2 => GamePostType.POST_TYPE_ACTIVITY,
            4 => GamePostType.POST_TYPE_INFO,
            _ => list.Activity?.Contains(notice) is true ? GamePostType.POST_TYPE_INFO : GamePostType.POST_TYPE_ANNOUNCE,
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
