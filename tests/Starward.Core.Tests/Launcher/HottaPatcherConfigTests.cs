using Starward.Core.Launcher.Hotta;
using System.Linq;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 异环游戏本体资源的版本配置（PatcherSDK）。
/// <para/>
/// 全部离线：样本照 2026-10 台服的真实文件删减而来，地址换成了假值。
/// </summary>
public class HottaPatcherConfigTests
{

    /// <summary>
    /// 外壳 Config.ini 的相关几段
    /// </summary>
    private const string ConfigIni = """
        [General]
        GameID=2000013

        [VERSION]
        Version=1.0.8.0928
        VersionInfoFileURL=https://patch1.example.invalid/hd/OBpublish_PC/launcher/Version.ini

        [Patcher]
        configPath=/ResFilesM/2000013/PatcherConfig/
        dataPath=/UserData
        patcher=Patcher/PatcherSDK
        ResRoot=/../Client
        """;


    private const string PatcherConfigJson = """
        {"appVersion":"0.0","minVersion":"0.0","gameResUrl":["https://patch1.example.invalid/clientRes","https://patch2.example.invalid/clientRes/"],"gameGetServerListUrl":["https://patch1.example.invalid"],"branchName":"PC_140","updateBranchName":"Publish_Update","appId":"2000013"}
        """;


    /// <summary>
    /// 设备清单删掉了；官方把本体放在最后，附加资源倒着列
    /// </summary>
    private const string VersionConfigXml = """
        <?xml version="1.0" ?>
        <config>
        	<AppVersion>0.0</AppVersion>
        	<ResVersion>1.4.3</ResVersion>
        	<UpdateResVersion>0.0</UpdateResVersion>
        	<Section>1.4</Section>
        	<ResSize>82220679395</ResSize>
        	<Hash>e86441</Hash>
        	<BaseVerson appVersion="0.0">
        		<Res section="0.104" version="0.104.3" Tag="pakchunk104" ResSize="1860148281"/>
        		<Res section="0.103" version="0.103.3" Tag="pakchunk103" ResSize="1753921802"/>
        		<Res section="0.102" version="0.102.3" Tag="pakchunk102" ResSize="1663403455"/>
        		<Res section="0.101" version="0.101.3" Tag="pakchunk101" ResSize="1755119671"/>
        		<Res section="1.4" version="1.4.3" Tag="baseTag" ResSize="82220679395"/>
        	</BaseVerson>
        	<Tag>baseTag</Tag>
        	<Extra>
        		<BaseTag>
        			<item name="baseTag"/>
        		</BaseTag>
        		<Compressed>1</Compressed>
        		<Encrypt>1</Encrypt>
        	</Extra>
        </config>
        """;


    [Fact]
    public void ParseConfigPath_ReadsThePatcherSection()
    {
        Assert.Equal("/ResFilesM/2000013/PatcherConfig/", HottaPatcherConfig.ParseConfigPath(ConfigIni));
        Assert.Null(HottaPatcherConfig.ParseConfigPath("[VERSION]\nVersion=1.0.8.0928"));
        Assert.Null(HottaPatcherConfig.ParseConfigPath(null));
    }


    /// <summary>
    /// 地址整理成不带结尾斜杠，版本配置在 {资源地址}/{分支}/Version/Windows/config.xml，主站在前
    /// </summary>
    [Fact]
    public void GetVersionConfigUrls_JoinsTheResourceUrlsWithTheBranch()
    {
        HottaPatcherSettings settings = HottaPatcherConfig.ParseSettings(PatcherConfigJson)!;

        Assert.Equal("PC_140", settings.Branch);
        Assert.Equal(
        [
            "https://patch1.example.invalid/clientRes/PC_140/Version/Windows/config.xml",
            "https://patch2.example.invalid/clientRes/PC_140/Version/Windows/config.xml",
        ], HottaPatcherConfig.GetVersionConfigUrls(settings));
    }


    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"gameResUrl":["https://patch1.example.invalid/clientRes"]}""")]
    [InlineData("""{"gameResUrl":[],"branchName":"PC_140"}""")]
    [InlineData("""{"gameResUrl":["http://patch1.example.invalid/clientRes"],"branchName":"PC_140"}""")]
    public void ParseSettings_RejectsWhatCannotBuildAnAddress(string json)
    {
        Assert.Null(HottaPatcherConfig.ParseSettings(json));
    }


    /// <summary>
    /// 本体排在最前，其余照标签名排；本体的大小就是顶层的 ResSize
    /// </summary>
    [Fact]
    public void ParseVersionConfig_PutsTheBaseFirst()
    {
        HottaResourceVersion version = HottaPatcherConfig.ParseVersionConfig(VersionConfigXml)!;

        Assert.Equal("1.4.3", version.Version);
        Assert.Equal(82220679395, version.Size);
        Assert.Equal(["baseTag", "pakchunk101", "pakchunk102", "pakchunk103", "pakchunk104"], version.Tags.Select(x => x.Tag));
        Assert.True(version.Tags[0].IsBase);
        Assert.Equal(version.Size, version.Tags[0].Size);
        Assert.All(version.Tags.Skip(1), x => Assert.False(x.IsBase));
        Assert.Equal("0.101.3", version.Tags[1].Version);
        Assert.Equal(1755119671, version.Tags[1].Size);
    }


    [Theory]
    [InlineData("")]
    [InlineData("<config><ResVersion>")]
    [InlineData("<config><ResSize>1</ResSize></config>")]
    public void ParseVersionConfig_ReturnsNullWithoutAVersion(string xml)
    {
        Assert.Null(HottaPatcherConfig.ParseVersionConfig(xml));
    }

}
