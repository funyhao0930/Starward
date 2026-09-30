using Starward.Core.Games.Kuro;
using Starward.Core.Launcher.Kuro;
using System.Text.Json;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 新启动器的分级配置怎么变成要下载的资源包，以及一次下载走新旧哪一份配置。
/// <para/>
/// 全部离线：JSON 照 2026-10-01 线上 3.7.0 的真实响应删减而来（补丁只留两个版本、显卡与语言各留几个），
/// 路径、大小与 MD5 都是真的。
/// </summary>
public class KuroResourcePackPlannerTests
{

    private const string OfficialJson = """
        {"cdnList":[
            {"P":1677,"url":"https://hw-pcdownload-qcloud.aki-game.net/","K1":1,"K2":1},
            {"P":0,"url":"https://pcdownload-huoshan.aki-game.net/","K1":1,"K2":1},
            {"P":2177,"url":"https://hw-pcdownload-akamai.aki-game.net/","K1":1,"K2":1},
            {"P":7276,"url":"https://hw-pcdownload-aws.aki-game.net/","K1":1,"K2":1}],
         "resourcePacks":{
            "uhd":{"version":"3.7.0","indexFile":"launcher/game/G153/50004/3.7.0/95231f15e9c2495888f4547bf0867358/uhd/indexFile.json","indexFileMd5":"fe9b4215b8526263aa42100f87213639",
                   "baseUrl":"launcher/game/G153/50004/3.7.0/95231f15e9c2495888f4547bf0867358/zip/","size":66043973056,"unCompressSize":66043973056,"patchType":"patch","patchConfig":[]},
            "sd":{"version":"3.7.0","indexFile":"launcher/game/G153/50004/3.7.0/95231f15e9c2495888f4547bf0867358/sd/indexFile.json","indexFileMd5":"ca4973bc6fa9d1422ccd9589b273981d",
                  "baseUrl":"launcher/game/G153/50004/3.7.0/95231f15e9c2495888f4547bf0867358/zip/","size":21973059446,"unCompressSize":21973059446,"patchType":"patch","patchConfig":[]},
            "hd":{"version":"3.7.0","indexFile":"launcher/game/G153/50004/3.7.0/95231f15e9c2495888f4547bf0867358/hd/indexFile.json","indexFileMd5":"599a4412646f7e10b0b1929e526234ae",
                  "baseUrl":"launcher/game/G153/50004/3.7.0/95231f15e9c2495888f4547bf0867358/zip/","size":45713445823,"unCompressSize":45713445823,"patchType":"patch","patchConfig":[]},
            "common":{"version":"3.7.0","indexFile":"launcher/game/G153/50004/3.7.0/95231f15e9c2495888f4547bf0867358/common/indexFile.json","indexFileMd5":"9006585db0525a7e86efd7265eb0a45a",
                      "baseUrl":"launcher/game/G153/50004/3.7.0/95231f15e9c2495888f4547bf0867358/zip/","size":40166686065,"unCompressSize":40166686065,"patchType":"patch",
                      "patchConfig":[
                        {"version":"3.6.0","indexFile":"launcher/game/G153/50004/3.7.0/95231f15e9c2495888f4547bf0867358/common/3.6.0/indexFile.json","indexFileMd5":"6ad30020107ee664d469f86a7855919f",
                         "baseUrl":"launcher/game/G153/50004/3.7.0/95231f15e9c2495888f4547bf0867358/common/3.6.0/resources","size":13100950712,"unCompressSize":38544352314,
                         "ext":{"requiredDiskSpace":6962079977,"deltaSize":-42485137795,"maxFileSize":1648703366}},
                        {"version":"3.6.1","indexFile":"launcher/game/G153/50004/3.7.0/95231f15e9c2495888f4547bf0867358/common/3.6.1/indexFile.json","indexFileMd5":"1b5d2aa2ad392d680520ca0c960c14a2",
                         "baseUrl":"launcher/game/G153/50004/3.7.0/95231f15e9c2495888f4547bf0867358/common/3.6.1/resources","size":13100438926,"unCompressSize":38361672290,
                         "ext":{"requiredDiskSpace":6962079985,"deltaSize":-42485143003,"maxFileSize":1648703366}}]}},
         "bundles":{
            "HD":{"resourcePacks":["common","hd"],"config":{"displayName":{"en":"HD","ja":"高画質","zh-Hans":"高清","zh-Hant":"高畫質"},"recommendGpu":["NVIDIA GeForce RTX 3060","NVIDIA GeForce RTX 4060"]}},
            "SD":{"resourcePacks":["common","sd"],"config":{"displayName":{"en":"SD","ja":"標準","zh-Hans":"流畅","zh-Hant":"流暢"},"recommendGpu":["NVIDIA GeForce GTX 1650","Intel(R) Iris(R) Xe Graphics"]}},
            "UHD":{"resourcePacks":["common","uhd"],"config":{"displayName":{"en":"UHD","ja":"最高","zh-Hans":"极致","zh-Hant":"極致"},"recommendGpu":["NVIDIA GeForce RTX 5080 Laptop GPU","NVIDIA GeForce RTX 4070"]}}},
         "config":{"keyFileCheckSwitch":1,"keyFileCheckList":["Client/Binaries/Win64/Client-Win64-Shipping.exe","Client/Binaries/Win64/Client-Win64-ShippingBase.dll"],"predownloadSwitch":1,
                   "experiment":{"repair":{"directoryIntegrityCheckList":"[{\"dir\":\"Client/Content/Paks\",\"exts\":[\"pak\",\"sig\"],\"recursive\":true}]"}}},
         "resourcesGray":null,
         "predownload":null}
        """;


    private static KuroOfficialGameIndex Index => JsonSerializer.Deserialize<KuroOfficialGameIndex>(OfficialJson)!;



    [Fact]
    public void Deserialize_ReadsPacksBundlesAndSettings()
    {
        KuroOfficialGameIndex index = Index;

        Assert.Equal(["uhd", "sd", "hd", "common"], index.ResourcePacks!.Keys);
        Assert.Equal(["common", "hd"], index.Bundles!["HD"].ResourcePacks);
        Assert.Equal("高畫質", index.Bundles["HD"].Config!.DisplayName!["zh-Hant"]);
        Assert.Equal(1, index.Config!.PredownloadSwitch);
        Assert.Single(KuroDownloadPlanner.ParseDirectoryIntegrityChecks(index.Config.Experiment!.Repair!.DirectoryIntegrityCheckList));
        Assert.Equal(4, KuroDownloadPlanner.GetCdnBases(index.CdnList).Count);
        Assert.Equal("3.7.0", KuroResourcePackPlanner.GetVersion(index.ResourcePacks));
    }


    [Fact]
    public void GetPackNames_PutsTheSharedPackFirstWithoutDuplicates()
    {
        KuroOfficialGameIndex index = Index;

        Assert.Equal(["common", "hd"], KuroResourcePackPlanner.GetPackNames(index.Bundles, ["hd"]));
        // 顺序照画质从高到低，与传入的顺序无关
        Assert.Equal(["common", "uhd", "sd"], KuroResourcePackPlanner.GetPackNames(index.Bundles, ["sd", "UHD"]));
    }


    [Fact]
    public void GetPackNames_ReturnsEmptyWhenATierIsMissing()
    {
        KuroOfficialGameIndex index = Index;
        index.Bundles!.Remove("SD");

        Assert.Empty(KuroResourcePackPlanner.GetPackNames(index.Bundles, ["hd", "sd"]));
        Assert.Empty(KuroResourcePackPlanner.GetPackNames(index.Bundles, []));
        Assert.Empty(KuroResourcePackPlanner.GetPackNames(null, ["hd"]));
    }


    [Fact]
    public void GetPacks_ReturnsTheDownloadConfigOfEachPack()
    {
        KuroOfficialGameIndex index = Index;

        IReadOnlyList<KuroResourcePack> packs = KuroResourcePackPlanner.GetPacks(index.ResourcePacks, index.Bundles, ["sd"])!;

        Assert.Equal(["common", "sd"], packs.Select(x => x.Name));
        Assert.Equal("launcher/game/G153/50004/3.7.0/95231f15e9c2495888f4547bf0867358/sd/indexFile.json", packs[1].Config.IndexFile);
        // 共用包带着从旧版本更新上来的补丁，分级包在 3.7.0 还没有
        Assert.NotNull(KuroDownloadPlanner.FindPatch(packs[0].Config, "3.6.1"));
        Assert.Null(KuroDownloadPlanner.FindPatch(packs[1].Config, "3.6.1"));
    }


    [Fact]
    public void GetPacks_ReturnsNullWhenAPackIsMissingOrHasNoIndex()
    {
        KuroOfficialGameIndex index = Index;
        index.ResourcePacks!["sd"].IndexFile = null;
        index.ResourcePacks.Remove("uhd");

        Assert.Null(KuroResourcePackPlanner.GetPacks(index.ResourcePacks, index.Bundles, ["sd"]));
        Assert.Null(KuroResourcePackPlanner.GetPacks(index.ResourcePacks, index.Bundles, ["uhd"]));
        Assert.NotNull(KuroResourcePackPlanner.GetPacks(index.ResourcePacks, index.Bundles, ["hd"]));
    }


    /// <summary>
    /// 与官方启动器选单上的数字对得上：极致 98.92 GB、高画质 79.98 GB、流畅 57.87 GB（其实是 GiB）
    /// </summary>
    [Fact]
    public void GetTierSizes_MatchesTheOfficialLauncher()
    {
        IReadOnlyList<KuroResourceTierSize> sizes = KuroResourcePackPlanner.GetTierSizes(Index);

        Assert.Equal(["uhd", "hd", "sd"], sizes.Select(x => x.Tier));
        Assert.Equal("98.92", (sizes[0].TotalBytes / (double)(1 << 30)).ToString("F2"));
        Assert.Equal("79.98", (sizes[1].TotalBytes / (double)(1 << 30)).ToString("F2"));
        Assert.Equal("57.87", (sizes[2].TotalBytes / (double)(1 << 30)).ToString("F2"));
        // 只属于这一档的大小不含共用包
        Assert.Equal(45713445823, sizes[1].TierBytes);
        Assert.Equal("極致", sizes[0].DisplayName!["zh-Hant"]);
    }


    /// <summary>
    /// HD 组合与旧版配置的整包是同一批文件，大小也一样
    /// </summary>
    [Fact]
    public void HdBundle_HasTheSameSizeAsTheLegacyIndex()
    {
        Assert.Equal(85880131888, KuroResourcePackPlanner.GetTierSizes(Index).Single(x => x.Tier == KuroResourceTier.HD).TotalBytes);
    }



    [Fact]
    public void GetPredownload_ReturnsNullWhenThereIsNone()
    {
        Assert.Null(KuroResourcePackPlanner.GetPredownload(Index));
    }


    [Fact]
    public void GetPredownload_ReadsAShapeLikeTheTopLevel()
    {
        string json = OfficialJson.Replace("\"predownload\":null", """
            "predownload":{"resourcePacks":{
                "common":{"version":"3.8.0","indexFile":"launcher/game/G153/50004/3.8.0/x/common/indexFile.json","baseUrl":"launcher/game/G153/50004/3.8.0/x/zip/","size":1,
                          "patchConfig":[{"version":"3.7.0","indexFile":"launcher/game/G153/50004/3.8.0/x/common/3.7.0/indexFile.json","baseUrl":"launcher/game/G153/50004/3.8.0/x/common/3.7.0/resources","size":1}]},
                "hd":{"version":"3.8.0","indexFile":"launcher/game/G153/50004/3.8.0/x/hd/indexFile.json","baseUrl":"launcher/game/G153/50004/3.8.0/x/zip/","size":1}}}
            """);
        KuroOfficialGameIndex index = JsonSerializer.Deserialize<KuroOfficialGameIndex>(json)!;

        KuroOfficialPredownload predownload = KuroResourcePackPlanner.GetPredownload(index)!;

        Assert.Equal("3.8.0", KuroResourcePackPlanner.GetVersion(predownload.ResourcePacks));
        // 没带自己的 bundles 与 CDN 时沿用顶层的
        Assert.Equal(["common", "hd"], KuroResourcePackPlanner.GetPacks(predownload.ResourcePacks, predownload.Bundles, ["hd"])!.Select(x => x.Name));
        Assert.Equal(4, predownload.CdnList!.Count);
    }


    /// <summary>
    /// 形状对不上时当成没有，而且不能让整份配置读不出来
    /// </summary>
    [Fact]
    public void GetPredownload_IgnoresUnknownShapes()
    {
        string json = OfficialJson.Replace("\"predownload\":null", "\"predownload\":{\"version\":\"3.8.0\",\"config\":{\"indexFile\":\"a\"}}");
        KuroOfficialGameIndex index = JsonSerializer.Deserialize<KuroOfficialGameIndex>(json)!;

        Assert.Null(KuroResourcePackPlanner.GetPredownload(index));

        string array = OfficialJson.Replace("\"predownload\":null", "\"predownload\":[1,2]");
        Assert.Null(KuroResourcePackPlanner.GetPredownload(JsonSerializer.Deserialize<KuroOfficialGameIndex>(array)!));
    }



    /// <summary>
    /// 只装 HD 时照旧走旧版配置：同一批文件，从 3.6.x 更新上来的补丁小一半以上
    /// </summary>
    [Fact]
    public void ChooseSource_KeepsHdOnlyOnTheLegacyIndex()
    {
        Assert.Equal(KuroDownloadSource.Legacy, KuroResourcePackPlanner.ChooseSource(["hd"], "3.7.0", "3.7.0"));
        Assert.Equal(KuroDownloadSource.Legacy, KuroResourcePackPlanner.ChooseSource(["hd"], "3.7.0", null));
    }


    /// <summary>
    /// 官方不再更新旧版配置、或读不到它时，改走分级配置
    /// </summary>
    [Fact]
    public void ChooseSource_LeavesAStaleOrMissingLegacyIndex()
    {
        Assert.Equal(KuroDownloadSource.Tiered, KuroResourcePackPlanner.ChooseSource(["hd"], "3.7.0", "3.8.0"));
        Assert.Equal(KuroDownloadSource.Tiered, KuroResourcePackPlanner.ChooseSource(["hd"], null, "3.7.0"));
    }


    [Fact]
    public void ChooseSource_UsesTheTieredIndexForOtherTiers()
    {
        Assert.Equal(KuroDownloadSource.Tiered, KuroResourcePackPlanner.ChooseSource(["sd"], "3.7.0", "3.7.0"));
        Assert.Equal(KuroDownloadSource.Tiered, KuroResourcePackPlanner.ChooseSource(["uhd", "hd"], "3.7.0", "3.7.0"));
        // 旧版配置给不了极致与流畅
        Assert.Null(KuroResourcePackPlanner.ChooseSource(["sd"], "3.7.0", null));
        Assert.Null(KuroResourcePackPlanner.ChooseSource(["hd"], null, null));
    }


    [Theory]
    [InlineData("3.7.0", "3.6.1", 1)]
    [InlineData("3.10.0", "3.9.2", 1)]
    [InlineData("3.7.0", "3.7.0", 0)]
    [InlineData("3.6.1", "3.7.0", -1)]
    public void CompareVersion_ComparesNumerically(string a, string b, int sign)
    {
        Assert.Equal(sign, Math.Sign(KuroResourcePackPlanner.CompareVersion(a, b)));
    }



    [Theory]
    [InlineData("hd", new[] { "hd" })]
    [InlineData("sd,UHD", new[] { "uhd", "sd" })]
    [InlineData("hd, hd;sd x", new[] { "hd", "sd" })]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void ParseTiers_NormalizesAndOrders(string? value, string[] expected)
    {
        Assert.Equal(expected, KuroResourceTier.Parse(value));
    }


    [Fact]
    public void FormatTiers_RoundTrips()
    {
        Assert.Equal("uhd,sd", KuroResourceTier.Format(["sd", "uhd", "sd"]));
        Assert.Equal(["uhd", "sd"], KuroResourceTier.Parse(KuroResourceTier.Format(["sd", "uhd"])));
    }

}
