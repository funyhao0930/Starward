using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Kuro;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 鸣潮官方启动器的背景图配置。
/// <para/>
/// 全部离线：JSON 是照真实响应的形状手写的，不碰真实接口；
/// 走 HTTP 的测试用假的 <see cref="HttpMessageHandler"/>。
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



    /// <summary>
    /// 背景图路径里的令牌要取自启动器配置的 <c>functionCode.background</c>。
    /// 官方随版本轮换这个令牌，而旧令牌的路径仍返回旧美术，写死就会停在旧版本上。
    /// </summary>
    [Fact]
    public async Task GetBackgroundAsync_UsesTokenFromLauncherConfig()
    {
        var handler = new StubHandler(url => url switch
        {
            _ when url.EndsWith("/launcher/launcher/50004_obOHXFrFanqsaIEOmuKroCcbZkQRBC7c/G153/index.json")
                => """{"functionCode":{"background":"newToken0001"}}""",
            _ when url.Contains("/background/newToken0001/zh-Hant.json") => VideoJson,
            _ => null,
        });
        var client = new KuroLauncherClient(new HttpClient(handler));

        KuroLauncherBackground? background = await client.GetBackgroundAsync("zh-Hant");

        Assert.NotNull(background);
        Assert.Equal("https://example.invalid/launcher/clientUpload/video0001.mp4", background.BackgroundFile);
    }


    /// <summary>
    /// 启动器配置取不到时退回内置令牌，而不是整个背景都不要了
    /// </summary>
    [Fact]
    public async Task GetBackgroundAsync_FallsBackToBuiltInTokenWhenConfigUnavailable()
    {
        var handler = new StubHandler(url => url.Contains("/background/") ? VideoJson : null);
        var client = new KuroLauncherClient(new HttpClient(handler));

        KuroLauncherBackground? background = await client.GetBackgroundAsync("zh-Hant");

        Assert.NotNull(background);
        Assert.Contains(handler.Requests, x => x.Contains("/background/lv1emIbKHn38mW6zgxiFqU3Uw8nwstj6/"));
    }


    /// <summary>
    /// 按网址回 JSON，回 null 的网址当 404
    /// </summary>
    private sealed class StubHandler(Func<string, string?> responder) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            lock (Requests)
            {
                Requests.Add(url);
            }
            string? json = responder(url);
            var response = json is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            return Task.FromResult(response);
        }
    }

}
