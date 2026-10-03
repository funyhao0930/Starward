using Starward.Core.HoYoPlay;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Starward.Core.Launcher.Gryphline;

/// <summary>
/// 把终末地的游戏内公告换成统一的 <see cref="GameNoticeBoard"/>。
/// <para/>
/// 这段转换不依赖任何 IO，值得单独测。
/// </summary>
public static class GryphlineNoticeMapper
{

    /// <summary>
    /// 游戏内的三个分页与顺序：版本更新、活动、新闻
    /// </summary>
    private static readonly (string Tab, string Type)[] Tabs =
    [
        (GryphlineBulletin.TAB_UPDATES, GamePostType.POST_TYPE_ANNOUNCE),
        (GryphlineBulletin.TAB_EVENTS, GamePostType.POST_TYPE_ACTIVITY),
        (GryphlineBulletin.TAB_NEWS, GamePostType.POST_TYPE_INFO),
    ];


    /// <summary>
    /// 换成公告板，没有可显示的公告时返回 null。
    /// <para/>
    /// 接口只返回已经上架的公告，不另筛时间；每个分页里按接口的顺序，那就是游戏内的顺序。
    /// </summary>
    public static GameNoticeBoard? ToNoticeBoard(GryphlineBulletinData? data)
    {
        if (data?.List is null)
        {
            return null;
        }
        var redDots = new HashSet<string>(data.OnlineList?.Where(x => x.NeedRedDot && x.Cid is not null).Select(x => x.Cid!) ?? []);
        var board = new GameNoticeBoard();
        foreach ((string tab, string type) in Tabs)
        {
            var items = new List<GameNoticeItem>();
            foreach (GryphlineBulletin bulletin in data.List.Where(x => x.Tab == tab))
            {
                if (ToNoticeItem(bulletin, redDots) is GameNoticeItem item)
                {
                    items.Add(item);
                }
            }
            if (items.Count > 0)
            {
                board.Tabs.Add(new GameNoticeTab { Type = type, Items = items });
            }
        }
        return board.Tabs.Count > 0 ? board : null;
    }


    private static GameNoticeItem? ToNoticeItem(GryphlineBulletin bulletin, HashSet<string> redDots)
    {
        if (string.IsNullOrWhiteSpace(bulletin.Cid) || string.IsNullOrWhiteSpace(bulletin.Title)
            || ToContentHtml(bulletin) is not string html)
        {
            return null;
        }
        return new GameNoticeItem
        {
            Id = bulletin.Cid,
            Title = UnescapeLineBreaks(bulletin.Title),
            Header = string.IsNullOrWhiteSpace(bulletin.Header) ? null : UnescapeLineBreaks(bulletin.Header),
            Date = bulletin.StartAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(bulletin.StartAt).ToLocalTime().ToString("MM/dd", CultureInfo.InvariantCulture) : "",
            NeedRedDot = redDots.Contains(bulletin.Cid),
            ContentHtml = html,
        };
    }


    /// <summary>
    /// 正文 HTML。图片类公告换成一张图，有链接时点图打开；认不出来的返回 null。
    /// </summary>
    private static string? ToContentHtml(GryphlineBulletin bulletin)
    {
        if (bulletin.Data.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }
        if (bulletin.DisplayType is GryphlineBulletin.DISPLAY_RICH_TEXT)
        {
            string? html = GetString(bulletin.Data, "html");
            return string.IsNullOrWhiteSpace(html) ? null : html;
        }
        if (bulletin.DisplayType is GryphlineBulletin.DISPLAY_PICTURE)
        {
            string? url = GetString(bulletin.Data, "url");
            if (!IsWebUrl(url))
            {
                return null;
            }
            string image = $"""<img src="{WebUtility.HtmlEncode(url)}">""";
            string? link = GetString(bulletin.Data, "link");
            return IsWebUrl(link) ? $"""<a href="{WebUtility.HtmlEncode(link)}">{image}</a>""" : image;
        }
        return null;
    }


    /// <summary>
    /// 标题里的换行是字面上的 <c>\n</c> 两个字符（「雪淞幽夢」\n版本日曆），游戏内显示成两行
    /// </summary>
    private static string UnescapeLineBreaks(string text)
    {
        return text.Replace("\\n", "\n").Trim();
    }


    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
    }


    private static bool IsWebUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https";
    }

}
