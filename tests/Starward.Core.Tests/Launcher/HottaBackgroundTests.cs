using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Hotta;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 异环的文件清单与背景配置。
/// <para/>
/// 全部离线：样本是照真实文件的形状手写的，地址换成了假值，不碰真实服务器。
/// </summary>
public class HottaBackgroundTests
{

    private const string VersionIni = """
        [VERSION]
        Version=1.0.8.0903
        Build=d13e45f6
        FileListURL=https://patch1.example.invalid/hd/OBpublish_PC/launcher/1.0.8.0903_1/AllFiles.xml

        [UPDATEINFO]
        """;


    /// <summary>
    /// 真实清单有上千个 File 节点，这里只留背景目录那三个，外加一个别处的文件用来验证筛选
    /// </summary>
    private const string AllFilesXml = """
        <?xml version='1.0' encoding='UTF-8'?>
        <All_Files>
          <BuildInfo Time="2026-09-03 11:28:55" />
          <ProductVersion Version="1.0.8.0903_1" />
          <Url BaseUrl="https://patch1.example.invalid/hd/OBpublish_PC/launcher" />
          <File Checksum="b93760ae7d9ecd1659bf3c26fab9515c" Path="/appsflyerlib_64.dll" Size="4574776" ZipChecksum="919d587ba615d1b05480481ad3cce963" ZipSize="1869752" />
          <File Checksum="449187947354d787c4005075b9f015dc" Path="/ResFilesM/2000013/bgimgs/bg.mp4" Size="6827732" ZipChecksum="a46fe9448e221f0993a3976f1a2bde6c" ZipSize="6615809" />
          <File Checksum="ae4d78dc159891ecd935b570eff7c544" Path="/ResFilesM/2000013/bgimgs/bg_0.png" Size="2658153" ZipChecksum="788afa538dcfe8916ebd61e4f829271c" ZipSize="2658301" />
          <File Checksum="748e82d04eef7894c932ee90c9ea140c" Path="/ResFilesM/2000013/bgimgs/config.json" Size="265" ZipChecksum="226f9a37e8c09e2486ddf26039242793" ZipSize="314" />
        </All_Files>
        """;


    /// <summary>
    /// 与真实 config.json 一致，包括那个把旧图列为作废的 discards
    /// </summary>
    private const string ConfigJson = """
        {
            "imgs": [{"file":"bg_0.png", "tipcolor":"#c0c0c0"}],
            "video":"bg.mp4",
            "noVideoBg":"bg_0.png",
            "interval":8000,
            "animation":false,
            "discards":["bg_0.jpg"]
        }
        """;


    private static HottaFileManifest Manifest()
    {
        return HottaLauncherManifest.ParseFileManifest(AllFilesXml, HottaBackgroundMapper.IsBackgroundFile)!;
    }


    private static HottaBackgroundConfig Config()
    {
        return JsonSerializer.Deserialize<HottaBackgroundConfig>(ConfigJson)!;
    }


    [Fact]
    public void ParseFileListUrl_ReadsTheManifestAddressFromVersionIni()
    {
        string? url = HottaLauncherManifest.ParseFileListUrl(VersionIni);

        Assert.Equal("https://patch1.example.invalid/hd/OBpublish_PC/launcher/1.0.8.0903_1/AllFiles.xml", url);
    }


    /// <summary>
    /// 与 Config.ini 一样，官方文件可能是 CRLF，行尾的回车不能让匹配落空
    /// </summary>
    [Fact]
    public void ParseFileListUrl_IsNotFooledByCarriageReturns()
    {
        string? url = HottaLauncherManifest.ParseFileListUrl(VersionIni.Replace("\n", "\r\n"));

        Assert.Equal("https://patch1.example.invalid/hd/OBpublish_PC/launcher/1.0.8.0903_1/AllFiles.xml", url);
    }


    [Fact]
    public void ParseFileListUrl_ReturnsNullWhenThereIsNothingToRead()
    {
        Assert.Null(HottaLauncherManifest.ParseFileListUrl(null));
        Assert.Null(HottaLauncherManifest.ParseFileListUrl(""));
        Assert.Null(HottaLauncherManifest.ParseFileListUrl("[VERSION]\nVersion=1.0.8.0903"));
    }


    /// <summary>
    /// 清单有上千个文件，解析时就该把用不着的筛掉
    /// </summary>
    [Fact]
    public void ParseFileManifest_KeepsOnlyTheFilesTheFilterWants()
    {
        HottaFileManifest manifest = Manifest();

        Assert.Equal("1.0.8.0903_1", manifest.Version);
        Assert.Equal("https://patch1.example.invalid/hd/OBpublish_PC/launcher", manifest.BaseUrl);
        Assert.Equal(3, manifest.Files.Count);
        Assert.DoesNotContain(manifest.Files, x => x.FileName == "appsflyerlib_64.dll");
    }


    /// <summary>
    /// 下载地址必须带 .zip：直接请求原始路径服务器返回 403
    /// </summary>
    [Fact]
    public void GetDownloadUrl_PointsAtTheZippedCopy()
    {
        HottaFileManifest manifest = Manifest();
        HottaManifestFile video = manifest.Files.First(x => x.FileName == "bg.mp4");

        Assert.Equal("https://patch1.example.invalid/hd/OBpublish_PC/launcher/1.0.8.0903_1/ResFilesM/2000013/bgimgs/bg.mp4.zip",
                     manifest.GetDownloadUrl(video));
    }


    [Fact]
    public void ParseFileManifest_ReturnsNullWhenItCannotBuildDownloadUrls()
    {
        Assert.Null(HottaLauncherManifest.ParseFileManifest(null));
        Assert.Null(HottaLauncherManifest.ParseFileManifest("not xml at all"));
        // 少了 BaseUrl 或版本号就拼不出地址，整份清单都没用
        Assert.Null(HottaLauncherManifest.ParseFileManifest("""<All_Files><ProductVersion Version="1.0" /></All_Files>"""));
        Assert.Null(HottaLauncherManifest.ParseFileManifest("""<All_Files><Url BaseUrl="https://x.invalid" /></All_Files>"""));
    }


    /// <summary>
    /// 必须按配置指名的文件名去挑。目录里还留着作废的 bg_0.jpg，
    /// 「挑最新的那个」正是本机路线踩过的坑。
    /// </summary>
    [Fact]
    public void SelectFiles_TakesTheFilesTheConfigNames()
    {
        var (poster, video) = HottaBackgroundMapper.SelectFiles(Config(), Manifest());

        Assert.Equal("bg_0.png", poster?.FileName);
        Assert.Equal("bg.mp4", video?.FileName);
    }


    /// <summary>
    /// noVideoBg 就是视频的首帧图，没有它才退回轮播图的第一张
    /// </summary>
    [Fact]
    public void GetPosterFileName_PrefersTheFirstFrameImage()
    {
        Assert.Equal("bg_0.png", Config().GetPosterFileName());

        var withoutPoster = JsonSerializer.Deserialize<HottaBackgroundConfig>("""
            {"imgs":[{"file":"slide_1.png"}],"video":"bg.mp4"}
            """)!;
        Assert.Equal("slide_1.png", withoutPoster.GetPosterFileName());

        var empty = JsonSerializer.Deserialize<HottaBackgroundConfig>("""{"video":"bg.mp4"}""")!;
        Assert.Null(empty.GetPosterFileName());
    }


    /// <summary>
    /// 缓存名带校验值：版本一换名字就换，也不会与别款游戏的 bg.mp4 重名
    /// </summary>
    [Fact]
    public void GetCacheFileName_IsScopedByChecksumAndKeepsTheExtension()
    {
        HottaManifestFile video = Manifest().Files.First(x => x.FileName == "bg.mp4");

        Assert.Equal("nte_449187947354d787c4005075b9f015dc.mp4", HottaBackgroundMapper.GetCacheFileName(video));
    }


    [Fact]
    public void ToGameBackground_PairsThePosterWithTheVideo()
    {
        var (poster, video) = HottaBackgroundMapper.SelectFiles(Config(), Manifest());
        GameBackground background = HottaBackgroundMapper.ToGameBackground(poster, video, "poster.png", "video.mp4")!;

        Assert.Equal(GameBackground.BACKGROUND_TYPE_VIDEO, background.Type);
        Assert.Equal("poster.png", background.Background.Url);
        Assert.Equal("video.mp4", background.Video.Url);
        // 异环没有压在视频上的版本标语图，显示层必须容得下它为空
        Assert.Null(background.Theme);
        // 换没换背景靠 Id 判断，校验值随版本变
        Assert.Equal("ae4d78dc159891ecd935b570eff7c544", background.Id);
    }


    /// <summary>
    /// 视频拿不到不致命，静态背景仍然可用
    /// </summary>
    [Fact]
    public void ToGameBackground_FallsBackToAStillImageWithoutAVideo()
    {
        var (poster, _) = HottaBackgroundMapper.SelectFiles(Config(), Manifest());
        GameBackground background = HottaBackgroundMapper.ToGameBackground(poster, null, "poster.png", null)!;

        Assert.Equal(GameBackground.BACKGROUND_TYPE_UNSPECIFIED, background.Type);
        Assert.Equal("poster.png", background.Background.Url);
    }


    /// <summary>
    /// 没有静态图就凑不出背景：显示层要拿它算主题色，也要拿它当停播后的静态背景
    /// </summary>
    [Fact]
    public void ToGameBackground_ReturnsNullWithoutAPoster()
    {
        Assert.Null(HottaBackgroundMapper.ToGameBackground(null, null, null, null));
        var (poster, video) = HottaBackgroundMapper.SelectFiles(Config(), Manifest());
        Assert.Null(HottaBackgroundMapper.ToGameBackground(poster, video, null, "video.mp4"));
    }

}
