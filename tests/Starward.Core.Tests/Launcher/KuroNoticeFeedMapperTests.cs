using Starward.Core.HoYoPlay;
using Starward.Core.Launcher;
using Starward.Core.Launcher.Kuro;
using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 鸣潮 3.8 起的游戏内公告板数据 <c>{lang}.json</c>：推荐、公告、资讯与日历。
/// <para/>
/// 全部离线：照 2026-11-16 线上 zh-Hant.json 的结构手写，地址换成了假值。
/// </summary>
public class KuroNoticeFeedMapperTests
{

    /// <summary>
    /// 2026-11-16 12:00 UTC+8
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.FromHours(8));


    private const string FeedJson = """
        {
          "game": [
            {
              "id": "50649", "redDotVersion": 0, "red": 1, "permanent": 0,
              "startTimeMs": 1794445200000, "endTimeMs": 1797883199000,
              "platform": [1,2,3,4,5,6,7,8], "channel": [], "whiteList": [],
              "tabTitle": "「舊夢凋零的百合」\n3.8版本內容說明",
              "tabBanner": "https://a.example.invalid/notice/image/a.png", "foldBanner": null,
              "content": "<div data-line=\"true\">x</div>",
              "tag": 1, "category": 1
            },
            {
              "id": "40000", "red": 1, "permanent": 0,
              "startTimeMs": 1700000000000, "endTimeMs": 1700000001000,
              "platform": [1], "channel": [], "whiteList": [],
              "tabTitle": "已經下架", "content": "", "tag": 1, "category": 1
            },
            {
              "id": "40001", "red": 1, "permanent": 0,
              "startTimeMs": 1794445200000, "endTimeMs": 1797883199000,
              "platform": [2,3], "channel": [], "whiteList": [],
              "tabTitle": "手機限定", "content": "", "tag": 1, "category": 1
            },
            {
              "id": "40002", "red": 1, "permanent": 0,
              "startTimeMs": 1794445200000, "endTimeMs": 1797883199000,
              "platform": [1], "channel": [], "whiteList": [800020562],
              "tabTitle": "測試帳號限定", "content": "", "tag": 1, "category": 1
            }
          ],
          "activity": [
            {
              "id": 50363, "red": 1, "permanent": 1,
              "startTimeMs": 1766606400000, "endTimeMs": 1861300800000,
              "platform": [1,2,3,4,5,6,7,8], "channel": [], "whiteList": [],
              "tabTitle": "《鳴潮》玩家社群資訊", "tabBanner": "https://a.example.invalid/notice/image/b.png",
              "content": "<a href=\"browser:https://discord.gg/x\">Discord</a>", "tag": 10, "category": 4
            }
          ],
          "recommend": [
            {
              "id": "50623", "red": 1, "permanent": 0,
              "startTimeMs": 1794427200000, "endTimeMs": 1796263199000,
              "platform": [1,2,3,4,5,6,7,8], "channel": [], "whiteList": [],
              "tabTitle": "[盛放於昨日夢土的花朵]\n角色活動喚取",
              "tabBanner": "https://a.example.invalid/notice/image/c.jpg", "foldBanner": "https://a.example.invalid/notice/image/d.jpg",
              "content": "<div>莉莉</div>", "tag": 7, "category": 3
            }
          ],
          "calendar": [
            {
              "id": "50648", "redDotVersion": 0, "red": 1,
              "startTimeMs": 1794427200000, "endTimeMs": 1797883199000,
              "isToWebp": null, "platform": [1,2,3,4,5,6,7,8], "channel": [], "whiteList": [],
              "backgroundUrl": "https://a.example.invalid/notice/image/calendar.jpg",
              "timeline": { "startDate": "2026-11-12 04:00:00", "endDate": "2026-12-22 03:59:59", "pointerStartDate": "2026-11-12 04:00:00", "x1": "15.1875", "x2": "95.6999" },
              "nodes": [
                { "relatedNoticeId": 50623, "relatedNoticeStatus": 1, "x1": "15.828", "y1": "9.358", "x2": "58.1885", "y2": "14.0116" },
                { "relatedNoticeId": 50638, "relatedNoticeStatus": 2, "x1": "59.1034", "y1": "9.4097", "x2": "95.6084", "y2": "14.0116" },
                { "relatedNoticeId": 50639, "relatedNoticeStatus": 1, "x1": "abc", "y1": "1", "x2": "2", "y2": "3" }
              ]
            }
          ]
        }
        """;


    private static GameNoticeBoard Map(DateTimeOffset? now = null)
    {
        return KuroNoticeMapper.ToNoticeBoard(JsonSerializer.Deserialize<KuroGameNoticeFeed>(FeedJson), now ?? Now)!;
    }


    /// <summary>
    /// 与游戏内一样：推荐、公告、资讯，顺序照清单；日历另放
    /// </summary>
    [Fact]
    public void Tabs_FollowGameOrder()
    {
        GameNoticeBoard board = Map();
        Assert.Equal([KuroNoticeMapper.RecommendTabType, GamePostType.POST_TYPE_ANNOUNCE, GamePostType.POST_TYPE_INFO], board.Tabs.Select(x => x.Type));
        Assert.Equal(["50623"], board.Tabs[0].Items.Select(x => x.Id));
        Assert.Equal(["50649"], board.Tabs[1].Items.Select(x => x.Id));
        Assert.Equal(["50363"], board.Tabs[2].Items.Select(x => x.Id));
    }


    [Fact]
    public void Expired_OtherPlatform_AndWhiteListed_AreHidden()
    {
        var ids = Map().Tabs.SelectMany(x => x.Items).Select(x => x.Id).ToList();
        Assert.DoesNotContain("40000", ids);
        Assert.DoesNotContain("40001", ids);
        Assert.DoesNotContain("40002", ids);
    }


    /// <summary>
    /// 正文随清单一起来，不必再去取
    /// </summary>
    [Fact]
    public void Item_CarriesContent()
    {
        GameNoticeItem item = Map().Tabs[0].Items[0];
        Assert.Equal("[盛放於昨日夢土的花朵]\n角色活動喚取", item.Title);
        Assert.Equal("https://a.example.invalid/notice/image/c.jpg", item.BannerUrl);
        Assert.Equal("<div>莉莉</div>", item.ContentHtml);
        Assert.Empty(item.ContentUrls);
        Assert.Equal(7, item.Tag);
        Assert.True(item.NeedRedDot);
    }


    /// <summary>
    /// 官方网页里长期公告从不亮红点
    /// </summary>
    [Fact]
    public void Permanent_HasNoRedDot()
    {
        GameNoticeBoard board = Map();
        Assert.False(board.Tabs[2].Items[0].NeedRedDot);
        Assert.True(board.Tabs[1].Items[0].NeedRedDot);
    }


    [Fact]
    public void Calendar_IsMapped_AndBadNodesSkipped()
    {
        GameNoticeCalendar calendar = Map().Calendar!;
        Assert.Equal("50648", calendar.Id);
        Assert.Equal("https://a.example.invalid/notice/image/calendar.jpg", calendar.BackgroundUrl);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 4, 0, 0, TimeSpan.FromHours(8)), calendar.TimelineStart);
        // 时间轴的起点就是清单里的上架时间，所以日期是 UTC+8
        Assert.Equal(1794427200000, calendar.TimelineStart.ToUnixTimeMilliseconds());
        Assert.Equal(15.1875, calendar.TimelineX1);
        Assert.Equal(2, calendar.Nodes.Count);
        Assert.Equal("50623", calendar.Nodes[0].NoticeId);
        Assert.Equal(GameNoticeCalendarNodeStatus.Online, calendar.Nodes[0].Status);
        Assert.Equal(GameNoticeCalendarNodeStatus.NotYetOnline, calendar.Nodes[1].Status);
        Assert.Equal(9.358, calendar.Nodes[0].Y1);
    }


    [Fact]
    public void Calendar_CountsAsReadableEntry()
    {
        Assert.Contains(("50648", true), Map().ReadableEntries);
    }


    /// <summary>
    /// 指针：时间轴之前停在起点，之后停在终点，中间线性内插
    /// </summary>
    [Fact]
    public void Calendar_PointerX()
    {
        GameNoticeCalendar calendar = Map().Calendar!;
        Assert.Equal(calendar.TimelineX1, calendar.GetPointerX(calendar.TimelineStart.AddDays(-3)));
        Assert.Equal(calendar.TimelineX2, calendar.GetPointerX(calendar.TimelineEnd.AddDays(3)));
        DateTimeOffset middle = calendar.TimelineStart + (calendar.TimelineEnd - calendar.TimelineStart) / 2;
        Assert.Equal((calendar.TimelineX1 + calendar.TimelineX2) / 2, calendar.GetPointerX(middle), 6);
    }


    [Fact]
    public void Calendar_OutsideItsWindow_IsDropped()
    {
        GameNoticeBoard board = Map(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(8)));
        Assert.Null(board.Calendar);
    }


    [Fact]
    public void Empty_ReturnsNull()
    {
        Assert.Null(KuroNoticeMapper.ToNoticeBoard((KuroGameNoticeFeed?)null, Now));
        Assert.Null(KuroNoticeMapper.ToNoticeBoard(JsonSerializer.Deserialize<KuroGameNoticeFeed>("{}"), Now));
    }

}
