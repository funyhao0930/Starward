using Starward.Core.Games;
using Starward.Core.Games.Manjuu;
using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Manjuu;
using Starward.Core.Tests.Games.Fakes;
using Xunit;

namespace Starward.Core.Tests.Games;

/// <summary>
/// 蓝色星原：旅谣。全部离线：cache.json 是照官方启动器写下的真实内容手写的，链接换成了假值。
/// </summary>
public class ManjuuGameTests
{

    private const string CacheJson = """
        {
        	"bgName": "bg_1795526199039.png",
        	"bgUrl": "https://example.invalid/azurpromilia/img/abc/1795489560832_KDFMEQUGQX（无字）OB启动器静态图.png",
        	"logoName": "logo_1795526199058.png",
        	"logoUrl": "https://example.invalid/azurpromilia/img/abc/1795489561859_WRKQXTOXOQlogo白.png",
        	"videoName": "video_1795526199078.dat",
        	"videoUrl": "https://example.invalid/azurpromilia/video/abc/1795489560995_XDYEKTJEAD蛮啾20261104-193809.mp4"
        }
        """;


    /// <summary>
    /// 不带官方的启动参数，游戏会弹「請使用啟動器啟動」；
    /// AzurPromilia.exe 又会以系统管理员身份重新拉起自己，所以文件名相同也要按进程名追踪
    /// </summary>
    [Fact]
    public async Task Launch_PassesLauncherArgumentAndTracksByProcessName()
    {
        string root = Path.Combine(Path.GetTempPath(), "StarwardTests", Guid.NewGuid().ToString("N"));
        try
        {
            GameDescriptor descriptor = ManjuuGameMapping.GetDescriptors()[0];
            string exe = Path.Combine(root, descriptor.ExecutableName!);
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "");

            var catalog = new SimpleGameCatalogProvider(ManjuuGameMapping.ProviderId, ManjuuGameMapping.GetDescriptors);
            var provider = new ManjuuGameLaunchProvider(catalog, new FakeGameLaunchSettings { DisableDlss = true });
            GameLaunchCommand command = await provider.CreateLaunchCommandAsync(
                descriptor.Key,
                new GameLaunchOptions { InstallPath = root },
                TestContext.Current.CancellationToken);

            Assert.Equal(exe, command.FileName);
            Assert.Equal(Path.GetDirectoryName(exe), command.WorkingDirectory);
            Assert.StartsWith("-start=azurpromilia_launcher", command.Arguments);
            Assert.Contains("-disableDLSS=1", command.Arguments);
            Assert.True(command.TrackByProcessName);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }


    [Theory]
    [InlineData("1.0.0.2646520", "1.0.0.2646520")]
    [InlineData(" 1.0.1.4 ", "1.0.1.4")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("1.0.0", "1.0.0")]
    public void ParseVersion_ReadsRegistryValue(string? text, string? expected)
    {
        Assert.Equal(expected, ManjuuGameMapping.ParseVersion(text)?.ToString());
    }


    [Fact]
    public void Cache_Parses()
    {
        ManjuuLauncherCache cache = ManjuuLauncherCache.Parse(CacheJson)!;
        Assert.Equal("bg_1795526199039.png", cache.BackgroundName);
        Assert.Equal("video_1795526199078.dat", cache.VideoName);
        Assert.EndsWith(".mp4", cache.VideoUrl);
        Assert.Null(ManjuuLauncherCache.Parse("not json"));
        Assert.Null(ManjuuLauncherCache.Parse(""));
    }


    /// <summary>
    /// 缓存名用媒体缓存里的 ASCII 文件名，视频从 .dat 改回 .mp4，静态图一律 png（裁过之后重新编码）
    /// </summary>
    [Fact]
    public void CacheNames_AreAsciiAndUseDisplayableExtensions()
    {
        ManjuuLauncherCache cache = ManjuuLauncherCache.Parse(CacheJson)!;
        Assert.Equal("manjuu_bg_1795526199039.png", ManjuuBackgroundMapper.GetPosterCacheName(cache));
        Assert.Equal("manjuu_video_1795526199078.mp4", ManjuuBackgroundMapper.GetVideoCacheName(cache));
        Assert.Null(ManjuuBackgroundMapper.GetPosterCacheName(null));
        Assert.Null(ManjuuBackgroundMapper.GetVideoCacheName(new ManjuuLauncherCache()));
    }


    /// <summary>
    /// 没有媒体缓存的文件名时退回 CDN 文件名
    /// </summary>
    [Fact]
    public void CacheNames_FallBackToCdnFileName()
    {
        var cache = new ManjuuLauncherCache { VideoUrl = "https://example.invalid/v/abc%20def.mp4?x=1" };
        Assert.Equal("manjuu_abc def.mp4", ManjuuBackgroundMapper.GetVideoCacheName(cache));
    }


    [Fact]
    public void ToGameBackground_VideoWithPoster()
    {
        ManjuuLauncherCache cache = ManjuuLauncherCache.Parse(CacheJson)!;
        GameBackground background = ManjuuBackgroundMapper.ToGameBackground(cache, "poster.png", "video.mp4")!;
        Assert.Equal(GameBackground.BACKGROUND_TYPE_VIDEO, background.Type);
        Assert.Equal("poster.png", background.Background.Url);
        Assert.Equal("video.mp4", background.Video.Url);
        Assert.Null(background.Theme);
        // ID 取自 CDN 文件名，美术换了它就跟着换
        Assert.Equal("1795489560995_XDYEKTJEAD蛮啾20261104-193809", background.Id);
    }


    [Fact]
    public void ToGameBackground_StillImageWhenNoVideo_NullWhenNoPoster()
    {
        ManjuuLauncherCache cache = ManjuuLauncherCache.Parse(CacheJson)!;
        GameBackground background = ManjuuBackgroundMapper.ToGameBackground(cache, "poster.png", null)!;
        Assert.Equal(GameBackground.BACKGROUND_TYPE_UNSPECIFIED, background.Type);
        Assert.Equal("poster.png", background.Background.Url);
        Assert.Null(ManjuuBackgroundMapper.ToGameBackground(cache, null, "video.mp4"));
        Assert.Null(ManjuuBackgroundMapper.ToGameBackground(null, "poster.png", null));
    }


    /// <summary>
    /// 启动器的背景图上方有一条透明的标题栏缺口、四角是圆角
    /// </summary>
    [Fact]
    public void CropRect_RemovesTransparentBandAndCorners()
    {
        const int width = 200, height = 120, band = 10;
        byte[] pixels = Pixels(width, height, (x, y) => y >= band);
        // 右下角的圆角
        pixels[((height - 1) * width + width - 1) * 4 + 3] = 0;

        var rect = ManjuuBackgroundMapper.GetCropRect(pixels, width, height);

        int inset = ManjuuBackgroundMapper.CornerInset;
        Assert.Equal((inset, band + inset, width - inset * 2, height - band - inset * 2), rect);
    }


    [Fact]
    public void CropRect_NullWhenFullyOpaqueOrInvalid()
    {
        Assert.Null(ManjuuBackgroundMapper.GetCropRect(Pixels(64, 36, (_, _) => true), 64, 36));
        Assert.Null(ManjuuBackgroundMapper.GetCropRect(Pixels(64, 36, (_, _) => false), 64, 36));
        Assert.Null(ManjuuBackgroundMapper.GetCropRect(new byte[10], 64, 36));
    }


    private static byte[] Pixels(int width, int height, Func<int, int, bool> opaque)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixels[(y * width + x) * 4 + 3] = opaque(x, y) ? (byte)255 : (byte)0;
            }
        }
        return pixels;
    }

}
