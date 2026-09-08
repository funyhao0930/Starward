using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Gryphline;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// GRYPHLINK 启动器的背景图接口。
/// <para/>
/// 全部离线：JSON 是照真实响应的形状手写的，不碰真实接口。
/// </summary>
public class GryphlineLauncherBackgroundTests
{

    /// <summary>
    /// 与聚合接口返回的形状一致，链接换成了假值
    /// </summary>
    private const string ResponseJson = """
        {"proxy_rsps":[{"kind":"get_main_bg_image","get_main_bg_image_rsp":{
          "data_version":"",
          "main_bg_image":{
            "url":"https://example.invalid/hg-utils/prod/aa/bb/poster0001.png",
            "md5":"3b179ecf7aee742109ba650af4821fdd",
            "video_url":"https://example.invalid/hg-utils/prod/cc/dd/video0001.mp4"}}}]}
        """;


    private static GryphlineMainBgImage? ParseImage(string json)
    {
        var response = JsonSerializer.Deserialize<GryphlineBatchProxyResponse>(json);
        return response?.ProxyResponses?[0].MainBgImageResponse?.MainBgImage;
    }


    [Fact]
    public void Deserialize_ReachesTheImageThroughTheProxyEnvelope()
    {
        GryphlineMainBgImage? image = ParseImage(ResponseJson);

        Assert.NotNull(image);
        Assert.Equal("https://example.invalid/hg-utils/prod/aa/bb/poster0001.png", image.Url);
        Assert.Equal("3b179ecf7aee742109ba650af4821fdd", image.Md5);
        Assert.Equal("https://example.invalid/hg-utils/prod/cc/dd/video0001.mp4", image.VideoUrl);
    }


    /// <summary>
    /// 问了但没有结果时接口只返回 kind，整个 rsp 字段都不在
    /// </summary>
    [Fact]
    public void Deserialize_HandlesAnEmptyProxyResponse()
    {
        Assert.Null(ParseImage("""{"proxy_rsps":[{"kind":"get_main_bg_image"}]}"""));
    }


    [Fact]
    public void ToGameBackgrounds_MapsTheVideoAndItsPoster()
    {
        GameBackground background = Assert.Single(GryphlineBackgroundMapper.ToGameBackgrounds(ParseImage(ResponseJson)));

        Assert.Equal(GameBackground.BACKGROUND_TYPE_VIDEO, background.Type);
        Assert.Equal("https://example.invalid/hg-utils/prod/cc/dd/video0001.mp4", background.Video.Url);
        Assert.Equal("https://example.invalid/hg-utils/prod/aa/bb/poster0001.png", background.Background.Url);
    }


    /// <summary>
    /// 这家不给版本标语，动态背景上没有叠图
    /// </summary>
    [Fact]
    public void ToGameBackgrounds_LeavesTheThemeEmpty()
    {
        GameBackground background = Assert.Single(GryphlineBackgroundMapper.ToGameBackgrounds(ParseImage(ResponseJson)));

        Assert.Null(background.Theme);
    }


    /// <summary>
    /// 接口没有背景图 ID，md5 比文件名更适合判断换没换
    /// </summary>
    [Fact]
    public void ToGameBackgrounds_TakesTheIdFromTheMd5()
    {
        GameBackground background = Assert.Single(GryphlineBackgroundMapper.ToGameBackgrounds(ParseImage(ResponseJson)));

        Assert.Equal("3b179ecf7aee742109ba650af4821fdd", background.Id);
    }


    [Fact]
    public void ToGameBackgrounds_FallsBackToTheFileNameWithoutAnMd5()
    {
        GryphlineMainBgImage image = ParseImage(ResponseJson)!;
        image.Md5 = null;

        GameBackground background = Assert.Single(GryphlineBackgroundMapper.ToGameBackgrounds(image));

        Assert.Equal("poster0001", background.Id);
    }


    [Fact]
    public void ToGameBackgrounds_FallsBackToAStillWhenThereIsNoVideo()
    {
        GryphlineMainBgImage image = ParseImage(ResponseJson)!;
        image.VideoUrl = null;

        GameBackground background = Assert.Single(GryphlineBackgroundMapper.ToGameBackgrounds(image));

        Assert.Equal(GameBackground.BACKGROUND_TYPE_UNSPECIFIED, background.Type);
        Assert.Null(background.Video);
    }


    /// <summary>
    /// 静态图不能少：显示层拿它算主题色，停播视频后也要靠它
    /// </summary>
    [Fact]
    public void ToGameBackgrounds_RefusesAVideoWithNoPoster()
    {
        GryphlineMainBgImage image = ParseImage(ResponseJson)!;
        image.Url = null;

        Assert.Empty(GryphlineBackgroundMapper.ToGameBackgrounds(image));
    }


    [Fact]
    public void ToGameBackgrounds_ReturnsNothingWhenThereIsNoResponse()
    {
        Assert.Empty(GryphlineBackgroundMapper.ToGameBackgrounds(null));
    }


    /// <summary>
    /// 这家用完整地区代码，不是库洛的脚本写法。
    /// 每种语言的美术都不一样，选错拿到的是另一张图。
    /// </summary>
    [Theory]
    [InlineData("zh-TW", "zh-tw")]
    [InlineData("zh-HK", "zh-tw")]
    [InlineData("zh-Hant-TW", "zh-tw")]
    [InlineData("zh-CN", "zh-cn")]
    [InlineData("zh-Hans-CN", "zh-cn")]
    [InlineData("ja-JP", "ja-jp")]
    [InlineData("ko-KR", "ko-kr")]
    [InlineData("en-GB", "en-us")]
    public void GetLanguageCode_MapsSystemCulturesOntoTheOfficialCodes(string culture, string expected)
    {
        Assert.Equal(expected, GryphlineLauncherClient.GetLanguageCode(new CultureInfo(culture)));
    }


    [Fact]
    public void GetLanguageCode_FallsBackToEnglishForUnsupportedLanguages()
    {
        Assert.Equal("en-us", GryphlineLauncherClient.GetLanguageCode(new CultureInfo("pl-PL")));
    }

}
