using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Kuro;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 鸣潮官方启动器的背景图配置。
/// <para/>
/// 全部离线：JSON 是照真实响应的形状手写的，不碰真实接口。
/// </summary>
public class KuroLauncherBackgroundTests
{

    /// <summary>
    /// 与官方接口返回的形状一致，链接换成了假值
    /// </summary>
    private const string VideoJson = """
        {"functionSwitch":1,
         "backgroundFile":"https://example.invalid/launcher/clientUpload/video0001.mp4",
         "backgroundFileType":2,
         "firstFrameImage":"https://example.invalid/launcher/clientUpload/frame0001.webp",
         "slogan":"https://example.invalid/launcher/clientUpload/slogan0001.png"}
        """;


    private static KuroLauncherBackground Parse(string json)
    {
        return JsonSerializer.Deserialize<KuroLauncherBackground>(json)!;
    }


    [Fact]
    public void Deserialize_ReadsEveryFieldOfAVideoBackground()
    {
        KuroLauncherBackground background = Parse(VideoJson);

        Assert.Equal(KuroLauncherBackground.FUNCTION_ON, background.FunctionSwitch);
        Assert.Equal(KuroLauncherBackground.FILE_TYPE_VIDEO, background.BackgroundFileType);
        Assert.Equal("https://example.invalid/launcher/clientUpload/video0001.mp4", background.BackgroundFile);
        Assert.Equal("https://example.invalid/launcher/clientUpload/frame0001.webp", background.FirstFrameImage);
        Assert.Equal("https://example.invalid/launcher/clientUpload/slogan0001.png", background.Slogan);
    }


    [Fact]
    public void ToGameBackgrounds_MapsAVideoBackgroundOntoTheThreeImageSlots()
    {
        List<GameBackground> backgrounds = KuroBackgroundMapper.ToGameBackgrounds(Parse(VideoJson));

        GameBackground background = Assert.Single(backgrounds);
        Assert.Equal(GameBackground.BACKGROUND_TYPE_VIDEO, background.Type);
        // 视频、首帧图、标语图各归各位，显示层才能播视频、算主题色、叠标语
        Assert.Equal("https://example.invalid/launcher/clientUpload/video0001.mp4", background.Video.Url);
        Assert.Equal("https://example.invalid/launcher/clientUpload/frame0001.webp", background.Background.Url);
        Assert.Equal("https://example.invalid/launcher/clientUpload/slogan0001.png", background.Theme.Url);
    }


    /// <summary>
    /// 接口没有服务器端 ID，上层靠它判断背景换了没有，
    /// 因此必须跟着 CDN 文件名走。
    /// </summary>
    [Fact]
    public void ToGameBackgrounds_TakesTheIdFromTheFileName()
    {
        GameBackground background = Assert.Single(KuroBackgroundMapper.ToGameBackgrounds(Parse(VideoJson)));

        Assert.Equal("video0001", background.Id);
    }


    [Fact]
    public void ToGameBackgrounds_GivesANewIdWhenTheArtworkChanges()
    {
        string updated = VideoJson.Replace("video0001", "video0002");

        string oldId = Assert.Single(KuroBackgroundMapper.ToGameBackgrounds(Parse(VideoJson))).Id;
        string newId = Assert.Single(KuroBackgroundMapper.ToGameBackgrounds(Parse(updated))).Id;

        Assert.NotEqual(oldId, newId);
    }


    /// <summary>
    /// 标语图是可选的，显示层允许动态背景没有叠图，
    /// 不该为了少一张图就牺牲动画
    /// </summary>
    [Fact]
    public void ToGameBackgrounds_KeepsTheVideoWhenTheSloganIsMissing()
    {
        KuroLauncherBackground background = Parse(VideoJson);
        background.Slogan = null;

        GameBackground result = Assert.Single(KuroBackgroundMapper.ToGameBackgrounds(background));

        Assert.Equal(GameBackground.BACKGROUND_TYPE_VIDEO, result.Type);
        Assert.Equal("https://example.invalid/launcher/clientUpload/video0001.mp4", result.Video.Url);
        Assert.Null(result.Theme);
    }


    /// <summary>
    /// 首帧图不能少：显示层拿它算主题色，停播视频后也要靠它
    /// </summary>
    [Fact]
    public void ToGameBackgrounds_RefusesAVideoWithNoFirstFrame()
    {
        KuroLauncherBackground background = Parse(VideoJson);
        background.FirstFrameImage = null;

        Assert.Empty(KuroBackgroundMapper.ToGameBackgrounds(background));
    }


    [Fact]
    public void ToGameBackgrounds_ReadsAStaticBackgroundStraightFromBackgroundFile()
    {
        KuroLauncherBackground background = new()
        {
            FunctionSwitch = KuroLauncherBackground.FUNCTION_ON,
            BackgroundFileType = KuroLauncherBackground.FILE_TYPE_IMAGE,
            BackgroundFile = "https://example.invalid/launcher/clientUpload/still0001.webp",
        };

        GameBackground result = Assert.Single(KuroBackgroundMapper.ToGameBackgrounds(background));

        Assert.Equal(GameBackground.BACKGROUND_TYPE_UNSPECIFIED, result.Type);
        Assert.Equal("https://example.invalid/launcher/clientUpload/still0001.webp", result.Background.Url);
    }


    [Fact]
    public void ToGameBackgrounds_ReturnsNothingWhenTheSwitchIsOff()
    {
        KuroLauncherBackground background = Parse(VideoJson);
        background.FunctionSwitch = 0;

        Assert.Empty(KuroBackgroundMapper.ToGameBackgrounds(background));
    }


    [Fact]
    public void ToGameBackgrounds_ReturnsNothingWhenThereIsNoResponse()
    {
        Assert.Empty(KuroBackgroundMapper.ToGameBackgrounds(null));
    }


    /// <summary>
    /// 官方用的是脚本写法，系统给的是地区写法，不换会 404
    /// </summary>
    [Theory]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData("zh-Hant-TW", "zh-Hant")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-Hans-CN", "zh-Hans")]
    [InlineData("ja-JP", "ja")]
    [InlineData("ko-KR", "ko")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("en-US", "en")]
    public void GetLanguageCode_MapsSystemCulturesOntoTheOfficialCodes(string culture, string expected)
    {
        Assert.Equal(expected, KuroLauncherClient.GetLanguageCode(new CultureInfo(culture)));
    }


    /// <summary>
    /// 官方启动器对不支持的语言也是退回英文
    /// </summary>
    [Fact]
    public void GetLanguageCode_FallsBackToEnglishForUnsupportedLanguages()
    {
        Assert.Equal("en", KuroLauncherClient.GetLanguageCode(new CultureInfo("pl-PL")));
    }

}
