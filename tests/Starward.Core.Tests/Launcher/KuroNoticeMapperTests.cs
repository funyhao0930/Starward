using Starward.Core.HoYoPlay;
using Starward.Core.Launcher;
using Starward.Core.Launcher.Kuro;
using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 鸣潮的游戏内公告。
/// <para/>
/// 全部离线：清单照 2026-10-03 线上 notice.json 的结构手写，地址换成了假值。
/// </summary>
public class KuroNoticeMapperTests
{

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1790800000000);


    private const string ListJson = """
        {
          "game": [
            {
              "id": "50612",
              "contentPrefix": ["https://a.example.invalid/gamenotice/content/G153/x/50612/", "https://b.example.invalid/gamenotice/content/G153/x/50612/"],
              "red": 1, "permanent": 0,
              "startTimeMs": 1790731200000, "endTimeMs": 1794427199000,
              "platform": [1,2,3,4,5,6,7,8], "channel": [], "whiteList": [],
              "tabTitle": { "zh-Hant": "「鏡鎖妄世，心照紅塵」\n3.7版本內容說明", "en": "Version 3.7 Notes" },
              "tabBanner": { "zh-Hant": ["https://a.example.invalid/notice/image/tw.jpg"], "en": ["https://a.example.invalid/notice/image/en.jpg"] },
              "tag": 1, "category": 1
            },
            {
              "id": "50606",
              "contentPrefix": ["https://a.example.invalid/gamenotice/content/G153/x/50606/"],
              "red": 1, "permanent": 0,
              "startTimeMs": 1790712000000, "endTimeMs": 1794427199000,
              "channel": [], "whiteList": [],
              "tabTitle": { "en": "[Event] Only in English" },
              "tag": 4, "category": 2
            },
            {
              "id": "50365",
              "contentPrefix": ["https://a.example.invalid/gamenotice/content/G153/x/50365/"],
              "red": 0, "permanent": 1,
              "startTimeMs": 1766606400000, "endTimeMs": 1767000000000,
              "channel": [], "whiteList": [],
              "tabTitle": { "zh-Hant": "《鳴潮》公平營運聲明" },
              "category": 1
            },
            {
              "id": "40000",
              "contentPrefix": ["https://a.example.invalid/gamenotice/content/G153/x/40000/"],
              "red": 1, "permanent": 0,
              "startTimeMs": 1700000000000, "endTimeMs": 1700000001000,
              "channel": [], "whiteList": [],
              "tabTitle": { "zh-Hant": "已經下架" },
              "category": 1
            },
            {
              "id": "40001",
              "contentPrefix": ["https://a.example.invalid/gamenotice/content/G153/x/40001/"],
              "red": 1, "permanent": 1,
              "channel": [], "whiteList": ["123456"],
              "tabTitle": { "zh-Hant": "測試帳號限定" },
              "category": 1
            },
            {
              "id": "40002",
              "contentPrefix": ["https://a.example.invalid/gamenotice/content/G153/x/40002/"],
              "red": 1, "permanent": 1,
              "channel": [12], "whiteList": [],
              "tabTitle": { "zh-Hant": "其他渠道限定" },
              "category": 1
            }
          ],
          "activity": [
            {
              "id": "50611",
              "contentPrefix": ["https://a.example.invalid/gamenotice/content/G153/x/50611/"],
              "red": 1, "permanent": 0,
              "startTimeMs": 1790712000000, "endTimeMs": 1794427199000,
              "channel": [], "whiteList": [],
              "tabTitle": { "zh-Hant": "3.7版本活動日曆" },
              "tag": 10, "category": 4
            }
          ]
        }
        """;


    private static GameNoticeBoard Map(string language = "zh-Hant")
    {
        return KuroNoticeMapper.ToNoticeBoard(JsonSerializer.Deserialize<KuroGameNoticeList>(ListJson), language, Now)!;
    }


    /// <summary>
    /// 与游戏内一样：「公告」是 game 整组（活动说明也在里面），「资讯」是 activity，顺序照清单
    /// </summary>
    [Fact]
    public void Tabs_FollowLists_InGameOrder()
    {
        GameNoticeBoard board = Map();
        Assert.Equal([GamePostType.POST_TYPE_ANNOUNCE, GamePostType.POST_TYPE_INFO], board.Tabs.Select(x => x.Type));
        Assert.Equal(["50612", "50606", "50365"], board.Tabs[0].Items.Select(x => x.Id));
        Assert.Equal(["50611"], board.Tabs[1].Items.Select(x => x.Id));
        Assert.Equal([1, 4, 0], board.Tabs[0].Items.Select(x => x.Tag));
        Assert.Equal(10, board.Tabs[1].Items[0].Tag);
    }


    [Fact]
    public void Expired_WhiteListed_AndOtherChannel_AreHidden()
    {
        var ids = Map().Tabs.SelectMany(x => x.Items).Select(x => x.Id).ToList();
        Assert.DoesNotContain("40000", ids);
        Assert.DoesNotContain("40001", ids);
        Assert.DoesNotContain("40002", ids);
    }


    [Fact]
    public void Permanent_IgnoresEndTime()
    {
        Assert.Contains(Map().Tabs[0].Items, x => x.Id == "50365");
    }


    [Fact]
    public void Item_UsesLanguage_FallsBackToEnglish()
    {
        GameNoticeBoard board = Map();
        GameNoticeItem first = board.Tabs[0].Items[0];
        Assert.Equal("「鏡鎖妄世，心照紅塵」\n3.7版本內容說明", first.Title);
        Assert.Equal("https://a.example.invalid/notice/image/tw.jpg", first.BannerUrl);
        Assert.Equal(2, first.ContentUrls.Count);
        Assert.Null(first.ContentHtml);
        Assert.True(first.NeedRedDot);
        Assert.Equal("[Event] Only in English", board.Tabs[0].Items[1].Title);
        Assert.False(board.Tabs[0].Items[2].NeedRedDot);
    }


    [Fact]
    public void Empty_ReturnsNull()
    {
        Assert.Null(KuroNoticeMapper.ToNoticeBoard(null, "en", Now));
        Assert.Null(KuroNoticeMapper.ToNoticeBoard(JsonSerializer.Deserialize<KuroGameNoticeList>("{}"), "en", Now));
    }


    [Fact]
    public void Content_ReturnsTextContent()
    {
        var content = JsonSerializer.Deserialize<KuroGameNoticeContent>("""{"noticeId":"50614","textContent":"<div>親愛的漂泊者：</div>","banner":"","textTitle":"已知問題"}""");
        Assert.Equal("<div>親愛的漂泊者：</div>", KuroNoticeMapper.ToContentHtml(content));
        Assert.Null(KuroNoticeMapper.ToContentHtml(new KuroGameNoticeContent()));
    }


    /// <summary>
    /// 线上 50612 的正文里 noticeId 是数字，50614 是字符串，两种都不能让整份读不出来
    /// </summary>
    [Fact]
    public void NumericIds_AreAccepted()
    {
        var content = JsonSerializer.Deserialize<KuroGameNoticeContent>("""{"noticeId":50612,"textContent":"<p>x</p>"}""");
        Assert.Equal("<p>x</p>", KuroNoticeMapper.ToContentHtml(content));
        var list = JsonSerializer.Deserialize<KuroGameNoticeList>("""{"game":[{"id":50612,"contentPrefix":["https://a.example.invalid/"],"permanent":1,"red":1,"tabTitle":{"en":"t"},"category":1}]}""");
        Assert.Equal("50612", list!.Game![0].Id);
    }

}
