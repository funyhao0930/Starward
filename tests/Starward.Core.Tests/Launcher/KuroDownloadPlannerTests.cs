using Starward.Core.Launcher.Kuro;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 鸣潮的下载配置怎么变成下载与补丁计划。
/// <para/>
/// 全部离线：JSON 照 2026-09 线上 3.6.1 的真实响应删减而来，
/// 路径与数字都是真的，只是清单只留几项。
/// </summary>
public class KuroDownloadPlannerTests
{

    private const string IndexJson = """
        {"default":{
            "cdnList":[
                {"K1":1,"K2":1,"P":1677,"url":"https://hw-pcdownload-qcloud.aki-game.net/"},
                {"K1":1,"K2":1,"P":7276,"url":"https://hw-pcdownload-aws.aki-game.net/"},
                {"K1":1,"K2":1,"P":0,"url":"https://pcdownload-huoshan.aki-game.net/"},
                {"K1":1,"K2":1,"P":2177,"url":"https://hw-pcdownload-akamai.aki-game.net"}],
            "resourcesBasePath":"launcher/game/G153/50004/3.6.1/pdzHTPViTKnSrdHgvYhBshNZCQEyWJOC/zip",
            "resources":"launcher/game/G153/50004/3.6.1/pdzHTPViTKnSrdHgvYhBshNZCQEyWJOC/resource.json",
            "config":{
                "indexFileMd5":"61d3bf4040983e8d3272a7d7937b9461",
                "unCompressSize":89180341357,
                "baseUrl":"launcher/game/G153/50004/3.6.1/pdzHTPViTKnSrdHgvYhBshNZCQEyWJOC/zip/",
                "size":89180341357,
                "patchType":"hotFix",
                "indexFile":"launcher/game/G153/50004/3.6.1/pdzHTPViTKnSrdHgvYhBshNZCQEyWJOC/resource/50004/3.6.1/indexFile.json",
                "version":"3.6.1",
                "patchConfig":[
                    {"indexFileMd5":"3d380927f126c7572398fba9035610ba","unCompressSize":89081945133,"ext":{},
                     "baseUrl":"launcher/game/G153/50004/3.6.0/kIILpILagqlxyFKWpexGltuOiOwdrYKr/zip/","size":89081945133,
                     "indexFile":"launcher/game/G153/50004/3.6.1/pdzHTPViTKnSrdHgvYhBshNZCQEyWJOC/resource/50004/3.6.1/1.0.0/indexFile.json","version":"1.0.0"},
                    {"unCompressSize":1934820470,"ext":{"requiredDiskSpace":29716936261,"maxFileSize":5222704655},
                     "baseUrl":"launcher/game/G153/50004/3.6.0/kIILpILagqlxyFKWpexGltuOiOwdrYKr/resource/50004/3.6.0/3.5.2/resources/","size":23459005942,
                     "indexFile":"launcher/game/G153/50004/3.6.1/pdzHTPViTKnSrdHgvYhBshNZCQEyWJOC/resource/50004/3.6.1/3.5.2/indexFile.json","version":"3.5.2"},
                    {"unCompressSize":1241324800,"ext":{},
                     "baseUrl":"launcher/game/G153/50004/3.6.1/pdzHTPViTKnSrdHgvYhBshNZCQEyWJOC/zip/","size":1241324800,
                     "indexFile":"launcher/game/G153/50004/3.6.1/pdzHTPViTKnSrdHgvYhBshNZCQEyWJOC/resource/50004/3.6.1/3.6.0/indexFile.json","version":"3.6.0"}]},
            "version":"3.6.1",
            "changelogVisible":0},
         "keyFileCheckSwitch":1,
         "predownloadSwitch":1,
         "keyFileCheckList":["Client/Binaries/Win64/Client-Win64-Shipping.exe","Wuthering Waves.exe"],
         "experiment":{"repair":{"directoryIntegrityCheckList":"[{\"dir\":\"Client/Content/Paks\",\"exts\":[\"pak\",\"sig\"],\"recursive\":true}]"}}}
        """;


    /// <summary>
    /// 3.5.2 → 3.6.1 的补丁清单，删到只剩两个差分包与两个普通文件。
    /// 其中 pakchunk0 同时出现在差分的新文件与热更新的普通文件里，正是要先打差分、后放普通文件的原因。
    /// </summary>
    private const string PatchJson = """
        {"resource":[
            {"dest":"3.5.2_3.6.0_group_1_1786200186326.krpdiff","md5":"a","size":100},
            {"dest":"Client/Content/Paks/pakchunk0-WindowsNoEditor.pak","md5":"b","size":200,"fromFolder":"launcher/game/G153/50004/3.6.1/pdzHTPViTKnSrdHgvYhBshNZCQEyWJOC/zip/"},
            {"dest":"3.5.2_3.6.0_group_15_1786202445570.krpdiff","md5":"c","size":513},
            {"dest":"Wuthering Waves.exe","md5":"d","size":483760}],
         "deleteFiles":["Client/Content/Paks/pakchunk73-WindowsNoEditor.pak"],
         "groupInfos":[
            {"dest":"3.5.2_3.6.0_group_1_1786200186326.krpdiff",
             "srcFiles":[{"dest":"Client/Content/Paks/pakchunk0-WindowsNoEditor.pak","md5":"8c55","size":5999444961}],
             "dstFiles":[{"dest":"Client/Content/Paks/pakchunk0-WindowsNoEditor.pak","md5":"2f40","size":6331982049}]},
            {"dest":"3.5.2_3.6.0_group_15_1786202445570.krpdiff",
             "srcFiles":[{"dest":"Client/Content/Paks/pakchunk28-WindowsNoEditor.pak","md5":"c042","size":158514680}],
             "dstFiles":[{"dest":"Client/Content/Paks/pakchunk28-WindowsNoEditor.pak","md5":"c8ea","size":158514680}]}],
         "applyTypes":["group"]}
        """;


    private static KuroLauncherGameIndex Index => JsonSerializer.Deserialize<KuroLauncherGameIndex>(IndexJson)!;

    private static KuroResourceIndex Patch => JsonSerializer.Deserialize<KuroResourceIndex>(PatchJson)!;



    /// <summary>
    /// 权重高的 CDN 在前，权重 0 的是备用，排最后；每个地址都整理成斜杠结尾
    /// </summary>
    [Fact]
    public void GetCdnBases_OrdersByPriorityAndKeepsTheBackupLast()
    {
        IReadOnlyList<string> bases = KuroDownloadPlanner.GetCdnBases(Index.Default!);

        Assert.Equal(
        [
            "https://hw-pcdownload-aws.aki-game.net/",
            "https://hw-pcdownload-akamai.aki-game.net/",
            "https://hw-pcdownload-qcloud.aki-game.net/",
            "https://pcdownload-huoshan.aki-game.net/",
        ], bases);
    }


    [Fact]
    public void GetFileUrl_EscapesSpacesButKeepsSeparators()
    {
        string url = KuroDownloadPlanner.GetFileUrl("https://cdn.example/", "launcher/game/G153/50004/3.6.1/abc/zip/", "Wuthering Waves.exe");
        Assert.Equal("https://cdn.example/launcher/game/G153/50004/3.6.1/abc/zip/Wuthering%20Waves.exe", url);

        url = KuroDownloadPlanner.GetFileUrl("https://cdn.example", "/zip", "Client/Content/Paks/pakchunk0-WindowsNoEditor.pak");
        Assert.Equal("https://cdn.example/zip/Client/Content/Paks/pakchunk0-WindowsNoEditor.pak", url);
    }


    /// <summary>
    /// 补丁按本机版本号查；查不到时返回 null，调用方改按完整清单比对
    /// </summary>
    [Fact]
    public void FindPatch_MatchesTheLocalVersion()
    {
        KuroLauncherGameConfig config = Index.Default!.Config!;

        Assert.Equal(23459005942, KuroDownloadPlanner.FindPatch(config, "3.5.2")?.Size);
        Assert.Equal(29716936261, KuroDownloadPlanner.FindPatch(config, "3.5.2")?.Ext?.RequiredDiskSpace);
        Assert.Equal(1241324800, KuroDownloadPlanner.FindPatch(config, "3.6.0")?.Size);
        Assert.Null(KuroDownloadPlanner.FindPatch(config, "2.9.9"));
        Assert.Null(KuroDownloadPlanner.FindPatch(config, null));
    }


    /// <summary>
    /// 差分包归差分，其余是普通文件；普通文件没写 fromFolder 时用补丁配置的 baseUrl
    /// </summary>
    [Fact]
    public void CreatePatchPlan_SeparatesDiffsFromPlainFiles()
    {
        const string patchFolder = "launcher/game/G153/50004/3.6.0/kIILpILagqlxyFKWpexGltuOiOwdrYKr/resource/50004/3.6.0/3.5.2/resources/";
        KuroPatchPlan plan = KuroDownloadPlanner.CreatePatchPlan(Patch, patchFolder);

        Assert.Equal(["3.5.2_3.6.0_group_1_1786200186326.krpdiff", "3.5.2_3.6.0_group_15_1786202445570.krpdiff"], plan.Diffs.Select(x => x.File.Dest));
        Assert.All(plan.Diffs, x => Assert.Equal(patchFolder, x.Folder));
        Assert.Equal("Client/Content/Paks/pakchunk28-WindowsNoEditor.pak", plan.Diffs[1].Group.DstFiles.Single().Dest);

        Assert.Equal(["Client/Content/Paks/pakchunk0-WindowsNoEditor.pak", "Wuthering Waves.exe"], plan.Files.Select(x => x.File.Dest));
        Assert.Equal("launcher/game/G153/50004/3.6.1/pdzHTPViTKnSrdHgvYhBshNZCQEyWJOC/zip/", plan.Files[0].Folder);
        Assert.Equal(patchFolder, plan.Files[1].Folder);

        Assert.Equal(["Client/Content/Paks/pakchunk73-WindowsNoEditor.pak"], plan.DeleteFiles);
        Assert.Equal(100 + 200 + 513 + 483760, plan.DownloadSize);
    }


    [Fact]
    public void IsPatchSupported_AcceptsGroupDiffs()
    {
        Assert.True(KuroDownloadPlanner.IsPatchSupported(Patch));
    }


    /// <summary>
    /// 没见过的应用方式、单文件 .krdiff、或没有说明的差分包，一律不打，改走完整清单
    /// </summary>
    [Fact]
    public void IsPatchSupported_RejectsWhatItCannotApply()
    {
        KuroResourceIndex unknownType = Patch;
        unknownType.ApplyTypes = ["group", "hpatch"];
        Assert.False(KuroDownloadPlanner.IsPatchSupported(unknownType));

        KuroResourceIndex krdiff = Patch;
        krdiff.Resource.Add(new KuroResourceFile { Dest = "Client/Binaries/Win64/Client-Win64-Shipping.exe.krdiff", Md5 = "e", Size = 1 });
        Assert.False(KuroDownloadPlanner.IsPatchSupported(krdiff));

        KuroResourceIndex orphan = Patch;
        orphan.GroupInfos!.RemoveAt(0);
        Assert.False(KuroDownloadPlanner.IsPatchSupported(orphan));
    }


    /// <summary>
    /// 热更新（3.6.0 → 3.6.1）的清单只有普通文件，没有 groupInfos 与 applyTypes
    /// </summary>
    [Fact]
    public void IsPatchSupported_AcceptsAHotfixWithOnlyPlainFiles()
    {
        KuroResourceIndex hotfix = JsonSerializer.Deserialize<KuroResourceIndex>("""
            {"resource":[{"dest":"Client/Binaries/Win64/libprotobuf.dll","md5":"4704fc61c94a4afede680bb96308ec5a","size":2317744}],"deleteFiles":[]}
            """)!;

        Assert.True(KuroDownloadPlanner.IsPatchSupported(hotfix));
        KuroPatchPlan plan = KuroDownloadPlanner.CreatePatchPlan(hotfix, "zip/");
        Assert.Empty(plan.Diffs);
        Assert.Single(plan.Files);
    }


    /// <summary>
    /// 预下载平时不在，出现时与 default 同形状
    /// </summary>
    [Fact]
    public void Deserialize_ReadsThePredownloadWhenPresent()
    {
        Assert.Null(Index.Predownload);
        Assert.Equal(1, Index.PredownloadSwitch);

        KuroLauncherGameIndex withPredownload = JsonSerializer.Deserialize<KuroLauncherGameIndex>("""
            {"default":{"version":"3.6.1"},
             "predownload":{"version":"3.7.0","cdnList":[{"P":1,"url":"https://cdn.example/"}],
                            "config":{"version":"3.7.0","indexFile":"a/indexFile.json","baseUrl":"a/zip/","size":1,"patchConfig":[{"version":"3.6.1","indexFile":"a/3.6.1/indexFile.json","baseUrl":"a/diff/","size":2}]}},
             "predownloadSwitch":1}
            """)!;

        Assert.Equal("3.7.0", withPredownload.Predownload?.Version);
        Assert.Equal(2, KuroDownloadPlanner.FindPatch(withPredownload.Predownload!.Config!, "3.6.1")?.Size);
    }


    [Fact]
    public void ParseDirectoryIntegrityChecks_ReadsTheStringifiedList()
    {
        IReadOnlyList<KuroDirectoryIntegrityCheck> checks = KuroDownloadPlanner.ParseDirectoryIntegrityChecks(Index.Experiment?.Repair?.DirectoryIntegrityCheckList);

        KuroDirectoryIntegrityCheck check = Assert.Single(checks);
        Assert.Equal("Client/Content/Paks", check.Dir);
        Assert.Equal(["pak", "sig"], check.Exts!);
        Assert.True(check.Recursive);

        Assert.Empty(KuroDownloadPlanner.ParseDirectoryIntegrityChecks("not json"));
        Assert.Empty(KuroDownloadPlanner.ParseDirectoryIntegrityChecks(null));
    }


    /// <summary>
    /// 没有补丁可用时，只下载与本机清单不同的文件
    /// </summary>
    [Fact]
    public void GetChangedFiles_ComparesMd5ByPath()
    {
        var latest = new KuroResourceIndex
        {
            Resource =
            [
                new() { Dest = "a.pak", Md5 = "1", Size = 1 },
                new() { Dest = "b.pak", Md5 = "2", Size = 1 },
                new() { Dest = "c.pak", Md5 = "3", Size = 1 },
            ],
        };
        var local = new KuroResourceIndex
        {
            Resource =
            [
                new() { Dest = "a.pak", Md5 = "1", Size = 1 },
                new() { Dest = "b.pak", Md5 = "old", Size = 1 },
            ],
        };

        Assert.Equal(["b.pak", "c.pak"], KuroDownloadPlanner.GetChangedFiles(latest, local).Select(x => x.Dest));
        Assert.Equal(3, KuroDownloadPlanner.GetChangedFiles(latest, null).Count);
    }

}
