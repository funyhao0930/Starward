using Starward.Core.Games;
using System;
using System.IO;
using Xunit;

namespace Starward.Core.Tests.Games;

/// <summary>
/// 只支持启动的游戏，启动页背景从本机文件里找。
/// </summary>
public class LocalGameArtworkTests : IDisposable
{

    private readonly string _root = Path.Combine(Path.GetTempPath(), "starward-artwork-" + Guid.NewGuid().ToString("N"));


    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
        GC.SuppressFinalize(this);
    }


    private string WriteFile(string relativePath, DateTime lastWriteTime)
    {
        string full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, [0]);
        File.SetLastWriteTime(full, lastWriteTime);
        return full;
    }


    private static GameDescriptor Descriptor(string[] backgroundPaths, string[] screenshotPaths) => new()
    {
        Key = new GameKey("test", "game", "channel"),
        DisplayName = "Test",
        BackgroundPaths = backgroundPaths,
        ScreenshotPaths = screenshotPaths,
    };


    [Fact]
    public void PrefersTheArtworkTheGameShipsOverThePlayersScreenshots()
    {
        // 截图更新，但游戏自带的美术仍然优先：它是这款游戏的门面，截图会随进度变
        WriteFile(@"art\bgimgs\bg_0.jpg", new DateTime(2026, 1, 1));
        WriteFile(@"shots\latest.png", new DateTime(2026, 8, 1));

        string? file = LocalGameArtwork.FindBackgroundImage(Descriptor(["art"], ["shots"]), _root);

        Assert.Equal("bg_0.jpg", Path.GetFileName(file));
    }


    [Fact]
    public void FallsBackToTheNewestScreenshotWhenTheGameShipsNoArtwork()
    {
        WriteFile(@"shots\old.png", new DateTime(2026, 7, 1));
        WriteFile(@"shots\sub\new.jpg", new DateTime(2026, 8, 1));

        string? file = LocalGameArtwork.FindBackgroundImage(Descriptor([], ["shots"]), _root);

        Assert.Equal("new.jpg", Path.GetFileName(file));
    }


    [Fact]
    public void IgnoresFilesThatAreNotImages()
    {
        WriteFile(@"art\manifest.json", new DateTime(2026, 8, 1));
        WriteFile(@"art\bgimgs\bg_0.jpg", new DateTime(2026, 1, 1));

        string? file = LocalGameArtwork.FindBackgroundImage(Descriptor(["art"], []), _root);

        Assert.Equal("bg_0.jpg", Path.GetFileName(file));
    }


    [Fact]
    public void FindsTheBackgroundVideoTheLauncherShips()
    {
        // bgimgs 里同时有静态图与动态背景，动态背景是启动器自带的视频
        WriteFile(@"art\bgimgs\bg_0.jpg", new DateTime(2026, 1, 1));
        WriteFile(@"art\bgimgs\bg.mp4", new DateTime(2026, 2, 1));

        string? image = LocalGameArtwork.FindBackgroundImage(Descriptor(["art"], []), _root);
        string? video = LocalGameArtwork.FindBackgroundVideo(Descriptor(["art"], []), _root);

        Assert.Equal("bg_0.jpg", Path.GetFileName(image));
        Assert.Equal("bg.mp4", Path.GetFileName(video));
    }


    [Fact]
    public void FindsNoBackgroundVideoOutsideTheLauncherArtwork()
    {
        // 视频只查启动器自带的美术目录，不查截图目录
        WriteFile(@"shots\cut.mp4", new DateTime(2026, 2, 1));

        Assert.Null(LocalGameArtwork.FindBackgroundVideo(Descriptor([], ["shots"]), _root));
    }


    [Fact]
    public void ReturnsNullWhenThereIsNothingToShow()
    {
        Assert.Null(LocalGameArtwork.FindBackgroundImage(Descriptor(["missing"], ["gone"]), _root));
        Assert.Null(LocalGameArtwork.FindBackgroundImage(null, _root));
        // 游戏没装时相对路径无从解析
        Assert.Null(LocalGameArtwork.FindBackgroundImage(Descriptor(["art"], []), null));
    }


    [Fact]
    public void AcceptsFullPathsForScreenshotsBecauseSomeGamesSaveToThePicturesFolder()
    {
        string full = Path.Combine(_root, "elsewhere");
        WriteFile(@"elsewhere\shot.png", new DateTime(2026, 8, 1));

        string? file = LocalGameArtwork.FindBackgroundImage(Descriptor([], [full]), installPath: null);

        Assert.Equal("shot.png", Path.GetFileName(file));
    }

}
