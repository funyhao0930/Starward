using Starward.Core.Games.Hotta;
using System;
using Xunit;

namespace Starward.Core.Tests.Games;

/// <summary>
/// 异环的版本号与官方版本文件地址，都从游戏自己的 ini 里读。
/// 全部离线：样本是照本机文件写下来的片段，不连网。
/// </summary>
public class HottaVersionInfoTests
{

    /// <summary>
    /// 游戏安装目录里 NTETW\Config\Config.ini 的形状
    /// </summary>
    private const string ConfigIni = """
        [General]
        Launcher=NTETWLauncher.exe
        Client=NTETWGame.exe

        [Game]
        Publisher=Iwplay
        DriverMinVersion=NVIDIA:591.86;https://www.nvidia.com/download/index.aspx

        [VERSION]
        Version=1.0.8.0727
        Build=0402a298
        VersionInfoFileURL=https://patchnte1.example.com/hd/OBpublish_PC/launcher/Version.ini
        serialNumber=1

        [UPDATE_CONFIG]
        BackupServer=https://patchnte2.example.com
        BackupVersionURL=https://patchnte2.example.com/hd/OBpublish_PC/launcher/Version.ini
        Branch=OBpublish_PC
        """;

    /// <summary>
    /// 官方公布的版本文件，与本机同一个写法
    /// </summary>
    private const string RemoteVersionIni = """
        [VERSION]
        Version=1.0.9.0801
        Build=0402a298
        FileListURL=https://patchnte1.example.com/hd/OBpublish_PC/launcher/1.0.9.0801_1/AllFiles.xml

        [UPDATEINFO]
        """;


    [Fact]
    public void ParseVersion_ReadsTheVersionFromEitherFile()
    {
        Assert.Equal(new Version(1, 0, 8, 727), HottaGameMapping.ParseVersion(ConfigIni));
        Assert.Equal(new Version(1, 0, 9, 801), HottaGameMapping.ParseVersion(RemoteVersionIni));
    }


    [Fact]
    public void ParseVersion_IsNotFooledByOtherKeysEndingInVersion()
    {
        // DriverMinVersion 也以 Version 结尾，取到它就会得到一个荒唐的版本号
        Version? version = HottaGameMapping.ParseVersion(ConfigIni);

        Assert.Equal(new Version(1, 0, 8, 727), version);
    }


    [Fact]
    public void ParseVersion_ReturnsNullWhenThereIsNothingToRead()
    {
        Assert.Null(HottaGameMapping.ParseVersion(null));
        Assert.Null(HottaGameMapping.ParseVersion(""));
        Assert.Null(HottaGameMapping.ParseVersion("[VERSION]\nBuild=0402a298"));
        Assert.Null(HottaGameMapping.ParseVersion("[VERSION]\nVersion=not a version"));
    }


    [Fact]
    public void ParseVersionInfoUrls_TakesThePrimaryFirstThenTheBackup()
    {
        var urls = HottaGameMapping.ParseVersionInfoUrls(ConfigIni);

        Assert.Equal(2, urls.Count);
        Assert.Equal("https://patchnte1.example.com/hd/OBpublish_PC/launcher/Version.ini", urls[0]);
        Assert.Equal("https://patchnte2.example.com/hd/OBpublish_PC/launcher/Version.ini", urls[1]);
    }


    [Fact]
    public void ParseVersionInfoUrls_IgnoresOtherAddressesInTheFile()
    {
        // BackupServer 与驱动下载页也是 https，但它们不是版本文件
        var urls = HottaGameMapping.ParseVersionInfoUrls(ConfigIni);

        Assert.DoesNotContain(urls, x => x.Contains("nvidia.com"));
        Assert.DoesNotContain(urls, x => x == "https://patchnte2.example.com");
    }


    [Fact]
    public void ParseVersionInfoUrls_ReturnsEmptyWhenTheGameConfiguresNone()
    {
        Assert.Empty(HottaGameMapping.ParseVersionInfoUrls(null));
        Assert.Empty(HottaGameMapping.ParseVersionInfoUrls("[VERSION]\nVersion=1.0.0.0"));
    }

}
