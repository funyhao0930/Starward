using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Kuro;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 鸣潮启动页的横幅与资讯。
/// <para/>
/// 全部离线：JSON 是照真实响应的形状手写的，不碰真实接口。
/// </summary>
public class KuroContentMapperTests
{

    /// <summary>
    /// 与官方接口返回的形状一致，链接换成了假值。
    /// 注意「活动」那一组现实中长期是关掉且内容为空的，这里照原样保留。
    /// </summary>
    private const string InformationJson = """
        {"guidance":{
            "desc":"暫無內容",
            "activity":{"title":"活動","sort":1,"functionSwitch":0,"contents":[]},
            "notice":{"title":"公告","sort":2,"functionSwitch":1,"contents":[
                {"content":"[身赴三途]角色活動喚取","jumpUrl":"https://example.invalid/n1","time":"09-09"},
                {"content":"【3.6版本】角色/武器活動喚取","jumpUrl":"https://example.invalid/n2","time":"09-08"}]},
            "news":{"title":"新聞","sort":3,"functionSwitch":1,"contents":[
                {"content":"共鳴者展示 | 景燃","jumpUrl":"https://example.invalid/w1","time":"09-09"}]}},
         "slideshow":[
            {"url":"https://example.invalid/slide0001.jpg","jumpUrl":"https://example.invalid/live","md5":"aaa111","carouselNotes":"3.7前瞻预告"},
            {"url":"https://example.invalid/slide0002.png","jumpUrl":"https://example.invalid/pv","md5":"bbb222","carouselNotes":"角色pv"}]}
        """;


    private static GameContent Map(string json)
    {
        return KuroContentMapper.ToGameContent(JsonSerializer.Deserialize<KuroLauncherInformation>(json))!;
    }


    [Fact]
    public void ToGameContent_MapsTheSlideshowOntoBanners()
    {
        GameContent content = Map(InformationJson);

        Assert.Equal(2, content.Banners.Count);
        Assert.Equal("https://example.invalid/slide0001.jpg", content.Banners[0].Image.Url);
        // 点击横幅打开的是 jumpUrl，不是图片本身
        Assert.Equal("https://example.invalid/live", content.Banners[0].Image.Link);
        // 接口没有轮播图 ID，校验值随图一起换，拿它当唯一标识
        Assert.Equal("aaa111", content.Banners[0].Id);
    }


    /// <summary>
    /// 三组的键是固定的英文名，分类不必从本地化的标题去猜
    /// </summary>
    [Fact]
    public void ToGameContent_MapsEachGuidanceGroupToItsPostType()
    {
        GameContent content = Map(InformationJson);

        List<GamePost> notices = content.Posts.Where(x => x.Type == GamePostType.POST_TYPE_ANNOUNCE).ToList();
        Assert.Equal(2, notices.Count);
        Assert.Equal("[身赴三途]角色活動喚取", notices[0].Title);
        Assert.Equal("https://example.invalid/n1", notices[0].Link);

        GamePost news = Assert.Single(content.Posts.Where(x => x.Type == GamePostType.POST_TYPE_INFO));
        Assert.Equal("共鳴者展示 | 景燃", news.Title);
    }


    /// <summary>
    /// 开关关掉的分组官方启动器自己也不显示
    /// </summary>
    [Fact]
    public void ToGameContent_SkipsGroupsWhoseSwitchIsOff()
    {
        GameContent content = Map(InformationJson);

        Assert.Empty(content.Posts.Where(x => x.Type == GamePostType.POST_TYPE_ACTIVITY));
    }


    /// <summary>
    /// 接口给的是 MM-dd，与米哈游的 MM/dd 统一
    /// </summary>
    [Fact]
    public void ToGameContent_NormalizesTheDateSeparator()
    {
        GameContent content = Map(InformationJson);

        Assert.Equal("09/09", content.Posts[0].Date);
    }


    /// <summary>
    /// 整份配置空掉时不能返回一个空壳，调用方要据此把这一块藏起来
    /// </summary>
    [Fact]
    public void ToGameContent_ReturnsNullWhenThereIsNothingToShow()
    {
        Assert.Null(KuroContentMapper.ToGameContent(null));
        Assert.Null(KuroContentMapper.ToGameContent(JsonSerializer.Deserialize<KuroLauncherInformation>("{}")));
    }

}
