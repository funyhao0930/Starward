using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Hotta;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 异环启动页的横幅与资讯。
/// <para/>
/// 全部离线：片段是照官网真实片段的结构手写的，链接换成了假值，不碰真实服务器。
/// </summary>
public class HottaContentMapperTests
{

    private static readonly Uri BannerUri = new("https://nte.example.invalid/CmsBanner/ubanner_A.html");

    private static readonly Uri NewsUri = new("https://nte.example.invalid/news/indexS_2.html");


    /// <summary>
    /// 与真实片段一样：前面一串轮播指示点，第一张已经下架，第二张长期挂着，
    /// 第三张只有 img 没有 source
    /// </summary>
    private const string BannerHtml = """
        <ol class="carousel-indicators">
            <li data-target="#ubanner_A" data-slide-to="0" class="active"></li>
            <li data-target="#ubanner_A" data-slide-to="1"></li>
        </ol>
        <div class="carousel-inner">
            <div class="carousel-item active" on="2026/06/24 11:00:00" off="2026/09/09 09:59:00">
                <a target="_blank" href="https://nte.example.invalid/news/view/20260721/c1c0dbf5.html">
                    <picture>
                <source media="(min-width: 993px)" srcset="/CmsBanner/BannerImg/2000213/desktop/old.webp" type="image/webp">
                <source media="(min-width: 1px)" srcset="/CmsBanner/BannerImg/2000213/phone/old.webp" type="image/webp">
                <img src="/CmsBanner/BannerImg/2000213/build/old.jpg" class="d-block w-100" alt="...">
            </picture>
                </a>
            </div>
            <div class="carousel-item " on="2026/09/30 10:00:00" off="2999/12/31 23:59:00">
                <a target="_blank" href="https://video.example.invalid/l61kcgQPIMo">
                    <picture>
                <source media="(min-width: 993px)" srcset="/CmsBanner/BannerImg/2000213/desktop/nte_260930_02.webp" type="image/webp">
                <source media="(min-width: 769px)" srcset="/CmsBanner/BannerImg/2000213/pad/nte_260930_02.webp" type="image/webp">
                <img src="/CmsBanner/BannerImg/2000213/build/nte_260930_02.jpg" class="d-block w-100" alt="...">
            </picture>
                </a>
            </div>
            <div class="carousel-item ">
                <a target="_blank" href="/event/BlackBird.html">
                    <img src="/CmsBanner/BannerImg/2000213/build/nte_260930_03.jpg" class="d-block w-100" alt="...">
                </a>
            </div>
        </div>
        """;


    /// <summary>
    /// 与真实片段一样：前半是轮播用的缩略图，同样的新闻又列了一次但没有日期；
    /// 后半才是带日期的列表，最后跟着一个「更多」链接
    /// </summary>
    private const string NewsHtml = """
        <html><head></head><body><div class="abs intelSlides">
          <div class="abs swiper intelSwiper">
            <div class="swiper-wrapper">
        <div class="swiper-slide system-up-row">
                <a class="systemurl" href="/news/view/20260929/40a419aa.html" title="《異環》9月30日停服維護公告" target="_self"><img class="news-img" src="/news/CmsnewsImg/2000213/build/a.png" alt="異環"></a>
              </div>
            </div>
          </div>
        </div>

        <div class="abs intelSwiperInfo">
        <div class="intelSlideCont system-single">
            <div class="newsdetails-badge">
              <p class="newsdetails-actbadge newsdetails-sysbadge">系統</p>
            </div>
            <a class="systemurl intelSlideTit" href="/news/view/20260929/40a419aa.html" target="_self" title="《異環》9月30日停服維護公告">《異環》9月30日停服維護公告</a>
            <a class="systemurl intelSlideDes" href="/news/view/20260929/40a419aa.html" target="_self" title="《異環》9月30日停服維護公告">
              <p>親愛的鑒定師：</p>
            </a>
            <div class="abs intelDate">2026-09-29</div>
          </div>

        <div class="intelSlideCont system-single">
            <div class="newsdetails-badge">
              <p class="newsdetails-actbadge newsdetails-sysbadge">系統</p>
            </div>
            <a class="systemurl intelSlideTit" href="/news/view/20260424/9c6e2e17.html" target="_self" title="《異環》公測福利活動&amp;獲取途徑一覽">《異環》公測福利活動&amp;獲取途徑一覽</a>
            <a class="systemurl intelSlideDes" href="/news/view/20260424/9c6e2e17.html" target="_self" title="《異環》公測福利活動&amp;獲取途徑一覽">
              <p>公測福利</p>
            </a>
            <div class="abs intelDate">2026-04-24</div>
          </div>

          <a href="https://nte.example.invalid/news/list_1.html" target="_blank" class="abs intelMore"></a>
        </div></body></html>
        """;


    /// <summary>
    /// 台湾时间 2026/10/01 12:00
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(8));


    [Fact]
    public void ParseBanners_PrefersTheDesktopSourceAndResolvesRelativePaths()
    {
        List<HottaBanner> banners = HottaContentMapper.ParseBanners(BannerHtml, BannerUri);

        // carousel-inner 与指示点都不是横幅
        Assert.Equal(3, banners.Count);
        // 第一个 source 是给桌面宽度的；站内的相对路径补成完整地址
        Assert.Equal("https://nte.example.invalid/CmsBanner/BannerImg/2000213/desktop/nte_260930_02.webp", banners[1].ImageUrl);
        Assert.Equal("https://video.example.invalid/l61kcgQPIMo", banners[1].Link);
        Assert.Equal("https://nte.example.invalid/event/BlackBird.html", banners[2].Link);
    }


    /// <summary>
    /// 没有 source 时退回 img，那是浏览器认不得 source 时用的后备
    /// </summary>
    [Fact]
    public void ParseBanners_FallsBackToTheImageWhenThereIsNoSource()
    {
        List<HottaBanner> banners = HottaContentMapper.ParseBanners(BannerHtml, BannerUri);

        Assert.Equal("https://nte.example.invalid/CmsBanner/BannerImg/2000213/build/nte_260930_03.jpg", banners[2].ImageUrl);
    }


    /// <summary>
    /// 上下架时间不带时区，是台湾时间
    /// </summary>
    [Fact]
    public void ParseBanners_ReadsThePublishWindowAsTaiwanTime()
    {
        HottaBanner banner = HottaContentMapper.ParseBanners(BannerHtml, BannerUri)[1];

        Assert.Equal(new DateTimeOffset(2026, 9, 30, 2, 0, 0, TimeSpan.Zero), banner.OnTime);
        Assert.Equal(new DateTimeOffset(2999, 12, 31, 23, 59, 0, TimeSpan.FromHours(8)), banner.OffTime);
    }


    /// <summary>
    /// 片段里留着已经下架的横幅，官方网页是在浏览器里按时间筛掉的；
    /// 没写上下架时间的当作一直挂着
    /// </summary>
    [Fact]
    public void ToGameContent_SkipsBannersOutsideTheirPublishWindow()
    {
        GameContent content = HottaContentMapper.ToGameContent(HottaContentMapper.ParseBanners(BannerHtml, BannerUri), [], Now)!;

        Assert.Equal(new[] { "nte_260930_02.webp", "nte_260930_03.jpg" }, content.Banners.Select(x => x.Image.Url.Split('/')[^1]));
        Assert.Equal("https://video.example.invalid/l61kcgQPIMo", content.Banners[0].Image.Link);
    }


    /// <summary>
    /// 上架时刻按 UTC+8 算：台湾 10:00 上架，UTC 01:59 还看不到，02:00 就看得到
    /// </summary>
    [Fact]
    public void ToGameContent_ComparesThePublishTimeInUtcPlusEight()
    {
        List<HottaBanner> banners = HottaContentMapper.ParseBanners(BannerHtml, BannerUri);

        GameContent before = HottaContentMapper.ToGameContent(banners, [], new DateTimeOffset(2026, 9, 30, 1, 59, 0, TimeSpan.Zero))!;
        GameContent after = HottaContentMapper.ToGameContent(banners, [], new DateTimeOffset(2026, 9, 30, 2, 0, 0, TimeSpan.Zero))!;

        Assert.DoesNotContain(before.Banners, x => x.Image.Url.EndsWith("nte_260930_02.webp"));
        Assert.Contains(after.Banners, x => x.Image.Url.EndsWith("nte_260930_02.webp"));
    }


    /// <summary>
    /// 前半的缩略图列表不算，否则同一条新闻会出现两次，而且没有日期
    /// </summary>
    [Fact]
    public void ParseNews_ReadsOnlyTheDatedList()
    {
        List<HottaNewsItem> items = HottaContentMapper.ParseNews(NewsHtml, NewsUri);

        Assert.Equal(2, items.Count);
        Assert.Equal("《異環》9月30日停服維護公告", items[0].Title);
        Assert.Equal("https://nte.example.invalid/news/view/20260929/40a419aa.html", items[0].Link);
        Assert.Equal("2026-09-29", items[0].Date);
    }


    [Fact]
    public void ParseNews_DecodesHtmlEntitiesInTheTitle()
    {
        List<HottaNewsItem> items = HottaContentMapper.ParseNews(NewsHtml, NewsUri);

        Assert.Equal("《異環》公測福利活動&獲取途徑一覽", items[1].Title);
    }


    /// <summary>
    /// 分类按分页给定，日期换成与米哈游一致的 MM/dd
    /// </summary>
    [Fact]
    public void ToGameContent_MapsEachListToItsPostTypeAndFormatsTheDate()
    {
        List<HottaNewsItem> notices = HottaContentMapper.ParseNews(NewsHtml, NewsUri);
        var events = new List<HottaNewsItem>
        {
            new() { Title = "黑羽的邀請函", Link = "https://nte.example.invalid/news/view/20260930/a3cb2e5c.html", Date = "2026-09-30" },
        };

        GameContent content = HottaContentMapper.ToGameContent([], [(GamePostType.POST_TYPE_ANNOUNCE, notices), (GamePostType.POST_TYPE_ACTIVITY, events)], Now)!;

        Assert.Equal(2, content.Posts.Count(x => x.Type == GamePostType.POST_TYPE_ANNOUNCE));
        GamePost activity = Assert.Single(content.Posts, x => x.Type == GamePostType.POST_TYPE_ACTIVITY);
        Assert.Equal("黑羽的邀請函", activity.Title);
        Assert.Equal("09/30", activity.Date);
        Assert.Equal("09/29", content.Posts[0].Date);
    }


    /// <summary>
    /// 同一篇新闻同时列在两个分页时只留第一次出现的
    /// </summary>
    [Fact]
    public void ToGameContent_KeepsAnArticleListedTwiceOnlyOnce()
    {
        List<HottaNewsItem> notices = HottaContentMapper.ParseNews(NewsHtml, NewsUri);

        GameContent content = HottaContentMapper.ToGameContent([], [(GamePostType.POST_TYPE_ANNOUNCE, notices), (GamePostType.POST_TYPE_INFO, notices)], Now)!;

        Assert.Equal(2, content.Posts.Count);
        Assert.All(content.Posts, x => Assert.Equal(GamePostType.POST_TYPE_ANNOUNCE, x.Type));
    }


    /// <summary>
    /// 什么都没有时不能返回一个空壳，调用方要据此把这一块藏起来
    /// </summary>
    [Fact]
    public void ToGameContent_ReturnsNullWhenThereIsNothingToShow()
    {
        Assert.Null(HottaContentMapper.ToGameContent(null, null, Now));
        Assert.Null(HottaContentMapper.ToGameContent(HottaContentMapper.ParseBanners("", BannerUri), [(GamePostType.POST_TYPE_INFO, HottaContentMapper.ParseNews(null, NewsUri))], Now));
    }

}
