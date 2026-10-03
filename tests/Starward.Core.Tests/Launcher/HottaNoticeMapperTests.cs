using Starward.Core.HoYoPlay;
using Starward.Core.Launcher;
using Starward.Core.Launcher.Hotta;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 异环的公告板与官网文章正文。
/// <para/>
/// 全部离线：文章页照 2026-10-03 官网文章的结构手写，地址换成了假值。
/// </summary>
public class HottaNoticeMapperTests
{

    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(8));


    private static List<(string, List<HottaNewsItem>)> NewsLists() =>
    [
        (GamePostType.POST_TYPE_ANNOUNCE,
        [
            new HottaNewsItem { Title = "《異環》9月30日停服維護公告", Link = "https://nte.example.invalid/news/view/20260929/a.html", Date = "2026-09-29" },
            new HottaNewsItem { Title = "《異環》儲值購點教學", Link = "https://nte.example.invalid/news/view/20260429/b.html", Date = "2026-04-29" },
        ]),
        (GamePostType.POST_TYPE_ACTIVITY,
        [
            // 同一篇也列在系统分页，只留第一次出现的
            new HottaNewsItem { Title = "《異環》9月30日停服維護公告", Link = "https://nte.example.invalid/news/view/20260929/a.html", Date = "2026-09-29" },
        ]),
        (GamePostType.POST_TYPE_INFO,
        [
            new HottaNewsItem { Title = "沒有日期", Link = "https://nte.example.invalid/news/view/x/c.html", Date = "" },
        ]),
    ];


    [Fact]
    public void Board_KeepsTabOrder_AndDropsDuplicates()
    {
        GameNoticeBoard board = HottaContentMapper.ToNoticeBoard(NewsLists(), Now)!;
        Assert.Equal([GamePostType.POST_TYPE_ANNOUNCE, GamePostType.POST_TYPE_INFO], board.Tabs.Select(x => x.Type));
        GameNoticeItem first = board.Tabs[0].Items[0];
        Assert.Equal("https://nte.example.invalid/news/view/20260929/a.html", first.Id);
        Assert.Equal(["https://nte.example.invalid/news/view/20260929/a.html"], first.ContentUrls);
        Assert.Equal(first.Id, first.BaseUrl);
        Assert.Equal("09/29", first.Date);
        Assert.Null(first.ContentHtml);
    }


    [Fact]
    public void RedDot_OnlyForRecentArticles()
    {
        GameNoticeBoard board = HottaContentMapper.ToNoticeBoard(NewsLists(), Now)!;
        Assert.True(board.Tabs[0].Items[0].NeedRedDot);
        Assert.False(board.Tabs[0].Items[1].NeedRedDot);
        Assert.False(board.Tabs[1].Items[0].NeedRedDot);
    }


    [Fact]
    public void Board_Empty_ReturnsNull()
    {
        Assert.Null(HottaContentMapper.ToNoticeBoard(null, Now));
        Assert.Null(HottaContentMapper.ToNoticeBoard([(GamePostType.POST_TYPE_ANNOUNCE, [])], Now));
    }


    private const string ArticleHtml = """
        <div class="abs articlePage">
          <div class="rel articleCont">
            <div class="abs articleHead">
              <h1 class="abs articleTitle">《異環》9月30日停服維護公告</h1>
              <p class="articleDate">2026-09-29</p>
            </div>
            <div class="abs article">
              <div class="articleContent"><p><picture>
                <source media="(min-width: 993px)" srcset="/news/CmsnewsImg/2000213/desktop/1.webp" type="image/webp">
                <img class="img-fluid" src="/news/CmsnewsImg/2000213/build/1.png"></picture></p>
        <p>親愛的鑒定師：</p>
        </div>
              <div class="rel articleBottom">
                <div class="abs auto articleBottomTxt">{bottom_text}</div>
              </div>
            </div>
          </div>
        </div>
        """;


    [Fact]
    public void Article_CutsContent_BeforeShareBar()
    {
        string content = HottaContentMapper.ParseArticleContent(ArticleHtml)!;
        Assert.StartsWith("<p><picture>", content);
        Assert.Contains("親愛的鑒定師：", content);
        Assert.DoesNotContain("{bottom_text}", content);
        Assert.DoesNotContain("articleTitle", content);
    }


    [Fact]
    public void Article_WithoutContainer_ReturnsNull()
    {
        Assert.Null(HottaContentMapper.ParseArticleContent("<html><body>404</body></html>"));
        Assert.Null(HottaContentMapper.ParseArticleContent(null));
    }

}
