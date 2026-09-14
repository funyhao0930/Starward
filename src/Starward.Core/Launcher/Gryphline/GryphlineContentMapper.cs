using Starward.Core.HoYoPlay;

namespace Starward.Core.Launcher.Gryphline;

/// <summary>
/// 把终末地的轮播图与公告换成应用统一使用的 <see cref="GameContent"/>。
/// <para/>
/// 换成米哈游那套模型之后，启动页的横幅与资讯控件一行都不用改。
/// 这段映射不依赖任何 IO，值得单独测。
/// </summary>
public static class GryphlineContentMapper
{

    /// <summary>
    /// 公告分页按顺序对应的分类。
    /// <para/>
    /// 接口没有给稳定的分类标识：<c>tabName</c> 已本地化，<c>tab_id</c> 更糟——
    /// 它随语言变化（繁中是 60/61/62，英文是 48/49/50），拿来当分类会直接错。
    /// 剩下能用的只有顺序，官方启动器自己也是照返回顺序排「公告 / 活动 / 新闻」。
    /// 多出来的分页一律并进资讯，不会丢内容。
    /// </summary>
    private static readonly string[] TabTypes =
    [
        GamePostType.POST_TYPE_ANNOUNCE,
        GamePostType.POST_TYPE_ACTIVITY,
        GamePostType.POST_TYPE_INFO,
    ];


    /// <summary>
    /// 换成统一的横幅与资讯，没有可用内容时返回 null
    /// </summary>
    public static GameContent? ToGameContent(GryphlineBannerResponse? bannerResponse, GryphlineAnnouncementResponse? announcementResponse)
    {
        var banners = new List<GameBanner>();
        foreach (GryphlineBanner banner in bannerResponse?.Banners ?? [])
        {
            if (string.IsNullOrWhiteSpace(banner.Url))
            {
                continue;
            }
            banners.Add(new GameBanner
            {
                Id = banner.Id ?? banner.Md5 ?? banner.Url,
                Image = new GameImage
                {
                    Url = banner.Url,
                    Link = banner.JumpUrl ?? "",
                    LoginStateInLink = banner.NeedToken,
                },
            });
        }

        var posts = new List<GamePost>();
        List<GryphlineAnnouncementTab> tabs = announcementResponse?.Tabs ?? [];
        for (int i = 0; i < tabs.Count; i++)
        {
            string type = i < TabTypes.Length ? TabTypes[i] : GamePostType.POST_TYPE_INFO;
            // 置顶的排在本组最前，其余保持接口给的顺序（已是按时间倒序）
            foreach (GryphlineAnnouncement item in (tabs[i].Announcements ?? []).OrderByDescending(x => x.Pin is not 0))
            {
                if (string.IsNullOrWhiteSpace(item.Content))
                {
                    continue;
                }
                posts.Add(new GamePost
                {
                    Id = item.Id ?? item.Content,
                    Type = type,
                    Title = item.Content,
                    Link = item.JumpUrl ?? "",
                    Date = FormatDate(item.StartTimestamp),
                });
            }
        }

        if (banners.Count is 0 && posts.Count is 0)
        {
            return null;
        }
        return new GameContent
        {
            Banners = banners,
            Posts = posts,
            SocialMediaList = [],
        };
    }


    /// <summary>
    /// 接口给的是 Unix 毫秒的字符串，换成与米哈游一致的 <c>MM/dd</c>。
    /// <para/>
    /// 按本机时区换算：这是给人看的发布日期，与玩家所在时区一致才合理。
    /// </summary>
    private static string FormatDate(string? timestamp)
    {
        if (long.TryParse(timestamp, out long ms))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("MM/dd");
        }
        return "";
    }

}
