using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Gryphline;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 终末地启动页的横幅与公告。
/// <para/>
/// 全部离线：JSON 是照真实响应的形状手写的，不碰真实接口。
/// </summary>
public class GryphlineContentMapperTests
{

    /// <summary>
    /// 与官方接口返回的形状一致，链接换成了假值。
    /// tab_id 故意用真实的繁中那一组（60/61/62），提醒它不是稳定的分类依据。
    /// </summary>
    private const string ResponseJson = """
        {"proxy_rsps":[
          {"kind":"get_banner","get_banner_rsp":{"banners":[
            {"url":"https://example.invalid/b1.jpg","jump_url":"https://example.invalid/news/1","md5":"aaa111","id":"1301","need_token":false},
            {"url":"https://example.invalid/b2.png","jump_url":"https://example.invalid/act/2","md5":"bbb222","id":"1343","need_token":true}]}},
          {"kind":"get_announcement","get_announcement_rsp":{"tabs":[
            {"tabName":"公告","tab_id":"60","announcements":[
              {"content":"「冬獵」特許尋訪","jump_url":"https://example.invalid/news/6172","start_ts":"1788235200000","id":"983","pin":0},
              {"content":"置頂的那一條","jump_url":"https://example.invalid/news/1","start_ts":"1788235200000","id":"984","pin":1}]},
            {"tabName":"活動","tab_id":"61","announcements":[
              {"content":"新版本討論","jump_url":"https://example.invalid/a/1","start_ts":"1788346800000","id":"1025","pin":0}]},
            {"tabName":"新聞","tab_id":"62","announcements":[
              {"content":"版本開發組通訊","jump_url":"https://example.invalid/n/1","start_ts":"1787392800000","id":"942","pin":0}]}]}}]}
        """;


    private static GameContent Map()
    {
        var response = JsonSerializer.Deserialize<GryphlineBatchProxyResponse>(ResponseJson)!;
        GryphlineBannerResponse? banners = response.ProxyResponses!
            .First(x => x.Kind == GryphlineLauncherClient.KIND_BANNER).BannerResponse;
        GryphlineAnnouncementResponse? announcements = response.ProxyResponses!
            .First(x => x.Kind == GryphlineLauncherClient.KIND_ANNOUNCEMENT).AnnouncementResponse;
        return GryphlineContentMapper.ToGameContent(banners, announcements)!;
    }


    [Fact]
    public void ToGameContent_MapsBannersWithTheirJumpLinks()
    {
        GameContent content = Map();

        Assert.Equal(2, content.Banners.Count);
        Assert.Equal("1301", content.Banners[0].Id);
        Assert.Equal("https://example.invalid/b1.jpg", content.Banners[0].Image.Url);
        Assert.Equal("https://example.invalid/news/1", content.Banners[0].Image.Link);
        // 要登录才能打开的链接照常保留，交给浏览器处理
        Assert.True(content.Banners[1].Image.LoginStateInLink);
    }


    /// <summary>
    /// 分类只能按分页顺序判断：tabName 已本地化，tab_id 更糟——它随语言变化。
    /// 这个测试就是在钉住那个假设，接口哪天改了顺序会在这里先炸。
    /// </summary>
    [Fact]
    public void ToGameContent_MapsTabsToPostTypesByOrder()
    {
        GameContent content = Map();

        Assert.Equal(2, content.Posts.Count(x => x.Type == GamePostType.POST_TYPE_ANNOUNCE));
        GamePost activity = Assert.Single(content.Posts.Where(x => x.Type == GamePostType.POST_TYPE_ACTIVITY));
        Assert.Equal("新版本討論", activity.Title);
        GamePost news = Assert.Single(content.Posts.Where(x => x.Type == GamePostType.POST_TYPE_INFO));
        Assert.Equal("版本開發組通訊", news.Title);
    }


    [Fact]
    public void ToGameContent_PutsPinnedAnnouncementsFirstInTheirGroup()
    {
        GameContent content = Map();

        List<GamePost> notices = content.Posts.Where(x => x.Type == GamePostType.POST_TYPE_ANNOUNCE).ToList();
        Assert.Equal("置頂的那一條", notices[0].Title);
    }


    /// <summary>
    /// 接口给的是 Unix 毫秒的字符串，换成与米哈游一致的 MM/dd
    /// </summary>
    [Fact]
    public void ToGameContent_FormatsTheTimestampAsMonthAndDay()
    {
        GameContent content = Map();

        GamePost post = content.Posts.First(x => x.Title == "「冬獵」特許尋訪");
        Assert.Matches(@"^\d{2}/\d{2}$", post.Date);
    }


    /// <summary>
    /// 多出来的分页不能丢，一律并进资讯
    /// </summary>
    [Fact]
    public void ToGameContent_FoldsExtraTabsIntoInformation()
    {
        var announcements = new GryphlineAnnouncementResponse
        {
            Tabs =
            [
                new GryphlineAnnouncementTab { TabName = "1", Announcements = [new GryphlineAnnouncement { Content = "a" }] },
                new GryphlineAnnouncementTab { TabName = "2", Announcements = [new GryphlineAnnouncement { Content = "b" }] },
                new GryphlineAnnouncementTab { TabName = "3", Announcements = [new GryphlineAnnouncement { Content = "c" }] },
                new GryphlineAnnouncementTab { TabName = "4", Announcements = [new GryphlineAnnouncement { Content = "d" }] },
            ],
        };
        GameContent content = GryphlineContentMapper.ToGameContent(null, announcements)!;

        Assert.Equal(4, content.Posts.Count);
        Assert.Equal(2, content.Posts.Count(x => x.Type == GamePostType.POST_TYPE_INFO));
    }


    /// <summary>
    /// 两个 kind 都没有结果时不能返回一个空壳，调用方要据此把这一块藏起来
    /// </summary>
    [Fact]
    public void ToGameContent_ReturnsNullWhenThereIsNothingToShow()
    {
        Assert.Null(GryphlineContentMapper.ToGameContent(null, null));
    }

}
