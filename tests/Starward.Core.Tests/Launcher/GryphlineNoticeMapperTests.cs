using Starward.Core.HoYoPlay;
using Starward.Core.Launcher;
using Starward.Core.Launcher.Gryphline;
using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 终末地的游戏内公告。
/// <para/>
/// 全部离线：返回照 2026-10-03 线上 bulletin/v2/aggregate 的结构手写，地址换成了假值。
/// </summary>
public class GryphlineNoticeMapperTests
{

    private const string ResponseJson = """
        {
          "code": 0,
          "data": {
            "topicCid": "0093", "type": 0, "platform": "Windows", "server": "2", "lang": "zh-tw",
            "onlineList": [
              { "cid": "8956", "version": 1789871803, "needRedDot": true, "needPopup": false },
              { "cid": "8945", "version": 1790404370, "needRedDot": false, "needPopup": false }
            ],
            "list": [
              {
                "cid": "8956", "type": 0, "tab": "events", "orderType": 1, "orderWeight": 1,
                "displayType": "rich_text", "startAt": 1789876800, "focus": 0,
                "title": "我們的大菲林！來襲！", "header": "「我們的大菲林！來襲！」饋贈活動說明", "jumpButton": null,
                "data": { "html": "<p><strong>▼//活動時間</strong></p>", "linkType": 1 }
              },
              {
                "cid": "8945", "type": 0, "tab": "news", "orderType": 1, "orderWeight": 4,
                "displayType": "picture", "startAt": 1790827200, "focus": 0,
                "title": "每日簽到", "header": "", "jumpButton": null,
                "data": { "url": "https://img.example.invalid/sign.png", "link": "https://link.example.invalid/?a=1&b=2", "linkType": 1 }
              },
              {
                "cid": "8958", "type": 0, "tab": "updates", "orderType": 2, "orderWeight": 1,
                "displayType": "rich_text", "startAt": 1790650800, "focus": 1,
                "title": "「雪淞幽夢」\\n版本更新說明", "header": "", "jumpButton": null,
                "data": { "html": "<p>更新內容</p>", "linkType": 0 }
              },
              {
                "cid": "9000", "type": 0, "tab": "updates", "displayType": "rich_text", "startAt": 1790650800,
                "title": "沒有正文", "data": { "html": "", "linkType": 0 }
              },
              {
                "cid": "9001", "type": 0, "tab": "unknown", "displayType": "rich_text", "startAt": 1790650800,
                "title": "不認識的分頁", "data": { "html": "<p>x</p>" }
              }
            ]
          },
          "msg": ""
        }
        """;


    private static GameNoticeBoard Map()
    {
        var response = JsonSerializer.Deserialize<GryphlineBulletinResponse>(ResponseJson)!;
        return GryphlineNoticeMapper.ToNoticeBoard(response.Data)!;
    }


    [Fact]
    public void Tabs_FollowInGameOrder_AndSkipUnknownOrEmpty()
    {
        GameNoticeBoard board = Map();
        Assert.Equal([GamePostType.POST_TYPE_ANNOUNCE, GamePostType.POST_TYPE_ACTIVITY, GamePostType.POST_TYPE_INFO], board.Tabs.Select(x => x.Type));
        Assert.Equal(["8958"], board.Tabs[0].Items.Select(x => x.Id));
        // 接口里的换行是字面上的 \n
        Assert.Equal("「雪淞幽夢」\n版本更新說明", board.Tabs[0].Items[0].Title);
        Assert.DoesNotContain(board.Tabs.SelectMany(x => x.Items), x => x.Id is "9000" or "9001");
    }


    [Fact]
    public void RichText_KeepsHtml_Header_AndRedDot()
    {
        GameNoticeItem item = Map().Tabs[1].Items[0];
        Assert.Equal("<p><strong>▼//活動時間</strong></p>", item.ContentHtml);
        Assert.Equal("「我們的大菲林！來襲！」饋贈活動說明", item.Header);
        Assert.True(item.NeedRedDot);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789876800).ToLocalTime().ToString("MM/dd"), item.Date);
    }


    [Fact]
    public void Picture_BecomesLinkedImage()
    {
        GameNoticeItem item = Map().Tabs[2].Items[0];
        Assert.Equal("""<a href="https://link.example.invalid/?a=1&amp;b=2"><img src="https://img.example.invalid/sign.png"></a>""", item.ContentHtml);
        Assert.Null(item.Header);
        Assert.False(item.NeedRedDot);
    }


    [Fact]
    public void Empty_ReturnsNull()
    {
        Assert.Null(GryphlineNoticeMapper.ToNoticeBoard(null));
        Assert.Null(GryphlineNoticeMapper.ToNoticeBoard(new GryphlineBulletinData()));
    }


    /// <summary>
    /// 与游戏内建浏览器缓存里的地址一致，只是不带登录用的 u8_token
    /// </summary>
    [Fact]
    public void BulletinPageUrl_MatchesInGamePage()
    {
        Assert.Equal("https://ef-webview.gryphline.com/page/game_bulletin?platform=Windows&channel=6&subChannel=6&lang=zh-tw&server=2",
                     GryphlineLauncherClient.GetBulletinPageUrl("zh-tw", "2"));
        Assert.Contains("lang=en-us", GryphlineLauncherClient.GetBulletinPageUrl("", "3"));
    }


    [Theory]
    [InlineData(8, "2")]
    [InlineData(9, "2")]
    [InlineData(5.5, "2")]
    [InlineData(1, "3")]
    [InlineData(0, "3")]
    [InlineData(-5, "3")]
    public void BulletinServer_FollowsTimeZone(double hours, string server)
    {
        Assert.Equal(server, GryphlineLauncherClient.GetBulletinServer(TimeSpan.FromHours(hours)));
    }

}
