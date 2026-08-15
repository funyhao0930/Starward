using Starward.Core.Games;
using System;
using System.IO;
using Xunit;

namespace Starward.Core.Tests.Games;

/// <summary>
/// 虚幻引擎游戏的画面设置。写入必须只碰已经存在的键，并且留一份备份。
/// </summary>
public class UnrealGameUserSettingsTests : IDisposable
{

    private readonly string _file = Path.Combine(Path.GetTempPath(), "starward-ue-" + Guid.NewGuid().ToString("N") + ".ini");


    /// <summary>
    /// 照本机文件写下来的片段
    /// </summary>
    private const string Sample = """
        [ScalabilityGroups]
        sg.ResolutionQuality=100.000000
        sg.ShadowQuality=1

        [/Script/Engine.GameUserSettings]
        bUseVSync=True
        ResolutionSizeX=2560
        ResolutionSizeY=1440
        LastUserConfirmedResolutionSizeX=2560
        LastUserConfirmedResolutionSizeY=1440
        FullscreenMode=1
        LastConfirmedFullscreenMode=2
        PreferredFullscreenMode=1
        FrameRateLimit=60.000000
        """;


    public void Dispose()
    {
        foreach (string file in new[] { _file, _file + ".starward.bak" })
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
        GC.SuppressFinalize(this);
    }


    private void WriteSample(string text = Sample) => File.WriteAllText(_file, text);


    [Fact]
    public void Read_TakesResolutionAndWindowMode()
    {
        WriteSample();

        GameResolutionSetting? setting = UnrealGameUserSettings.Read(_file);

        Assert.NotNull(setting);
        Assert.Equal(2560, setting!.Value.Width);
        Assert.Equal(1440, setting.Value.Height);
        // FullscreenMode=1 是无边框全屏，仍然算全屏
        Assert.True(setting.Value.FullScreen);
    }


    [Fact]
    public void Read_TreatsModeTwoAsWindowed()
    {
        WriteSample(Sample.Replace("FullscreenMode=1", "FullscreenMode=2"));

        Assert.False(UnrealGameUserSettings.Read(_file)!.Value.FullScreen);
    }


    [Fact]
    public void Read_ReturnsNullWhenThereIsNoFileOrNoResolution()
    {
        Assert.Null(UnrealGameUserSettings.Read(_file));
        Assert.Null(UnrealGameUserSettings.Read(null));
        WriteSample("[/Script/Engine.GameUserSettings]\r\nbUseVSync=True");
        Assert.Null(UnrealGameUserSettings.Read(_file));
    }


    [Fact]
    public void Write_UpdatesTheConfirmedValuesToo()
    {
        WriteSample();

        Assert.True(UnrealGameUserSettings.Write(_file, new GameResolutionSetting(1920, 1080, true)));

        string text = File.ReadAllText(_file);
        // 引擎载入时用的是 LastUserConfirmed*，只改前一对会被还原
        Assert.Contains("ResolutionSizeX=1920", text);
        Assert.Contains("LastUserConfirmedResolutionSizeX=1920", text);
        Assert.Contains("ResolutionSizeY=1080", text);
        Assert.Contains("LastUserConfirmedResolutionSizeY=1080", text);
    }


    [Fact]
    public void Write_LeavesTheWindowModeAloneWhenItAlreadyMatches()
    {
        WriteSample();

        UnrealGameUserSettings.Write(_file, new GameResolutionSetting(1920, 1080, true));

        // 本来就是全屏（模式 1），玩家也许特意选的，不该被改成别的全屏方式
        string text = File.ReadAllText(_file);
        Assert.Contains("FullscreenMode=1", text);
        Assert.Contains("LastConfirmedFullscreenMode=2", text);
    }


    [Fact]
    public void Write_SwitchesEveryModeKeyWhenTheChoiceChanges()
    {
        WriteSample();

        UnrealGameUserSettings.Write(_file, new GameResolutionSetting(1920, 1080, false));

        string text = File.ReadAllText(_file);
        Assert.Contains("FullscreenMode=2", text);
        Assert.Contains("LastConfirmedFullscreenMode=2", text);
        Assert.Contains("PreferredFullscreenMode=2", text);
    }


    [Fact]
    public void Write_NeverAddsKeysTheGameDidNotWrite()
    {
        WriteSample("""
            [/Script/Engine.GameUserSettings]
            ResolutionSizeX=1280
            ResolutionSizeY=720
            """);

        UnrealGameUserSettings.Write(_file, new GameResolutionSetting(1920, 1080, false));

        string text = File.ReadAllText(_file);
        Assert.Contains("ResolutionSizeX=1920", text);
        Assert.DoesNotContain("LastUserConfirmedResolutionSizeX", text);
        Assert.DoesNotContain("FullscreenMode", text);
        // 其他内容原样保留
        Assert.Contains("[/Script/Engine.GameUserSettings]", text);
    }


    [Fact]
    public void Write_KeepsABackupBeforeTouchingTheFile()
    {
        WriteSample();

        UnrealGameUserSettings.Write(_file, new GameResolutionSetting(1920, 1080, false));

        Assert.Equal(Sample, File.ReadAllText(_file + ".starward.bak"));
    }


    [Fact]
    public void Write_RefusesNonsenseAndMissingFiles()
    {
        WriteSample();
        Assert.False(UnrealGameUserSettings.Write(_file, new GameResolutionSetting(0, 1080, true)));
        Assert.False(UnrealGameUserSettings.Write(null, new GameResolutionSetting(1920, 1080, true)));
        // 值没有变化时不该改写文件，也就不会留下备份
        Assert.False(UnrealGameUserSettings.Write(_file, new GameResolutionSetting(2560, 1440, true)));
        Assert.False(File.Exists(_file + ".starward.bak"));
    }

}
