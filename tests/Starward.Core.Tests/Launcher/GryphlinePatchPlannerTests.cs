using Starward.Core.Launcher.Gryphline;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 终末地补丁更新的计算。patch.json 与接口响应照真实的形状手写，路径与 MD5 是假值。
/// </summary>
public class GryphlinePatchPlannerTests
{

    private const string PatchInfoJson = """
        {"version":"1.6.3","vfs_base_path":"Endfield_Data/StreamingAssets/VFS","files":[
          {"name":"AA/NEW1.chk","name_path":"","md5":"m1","size":300,"diffType":1,
           "patch":[{"base_file":"AA/OLD1.chk","base_file_path":"","base_md5":"b1","base_size":200,"patch":"diff_1.6.3_1.5.3/AA_NEW1.chk_patch","patch_path":"","patch_size":50}]},
          {"name":"AA/AA.blc","name_path":"","md5":"m2","size":40,"diffType":2,
           "local_path":"vfs_files/files/Endfield_Data/StreamingAssets/VFS/AA/AA.blc"},
          {"name":"BB/SAME.chk","name_path":"","md5":"m3","size":1000,"diffType":1}
        ]}
        """;


    [Fact]
    public void LatestGame_ReadsPatchAndPredownload()
    {
        const string json = """
            {"action":1,"version":"1.6.3","request_version":"1.5.3",
             "pkg":{"packs":[],"file_path":"https://example.invalid/1.6.3/files","game_files_md5":"abc"},
             "patch":{"url":"https://example.invalid/p.zip","package_size":"30","total_size":"60",
                      "patches":[{"url":"https://example.invalid/v2/x_1_6_3_1_5_3.zip.001","md5":"p1","package_size":"20"},
                                 {"url":"https://example.invalid/v2/x_1_6_3_1_5_3.zip.002","md5":"p2","package_size":"10"}],
                      "v2_patch_info_url":"https://example.invalid/patch.json","v2_patch_info_md5":"pj","cd_key":"KEY"},
             "pre_patch":{"version":"1.7.0","package_size":"5","patches":[{"url":"https://example.invalid/pre.zip.001","md5":"q","package_size":"5"}]}}
            """;
        var latest = JsonSerializer.Deserialize<GryphlineLatestGame>(json)!;

        Assert.Equal("1.5.3", latest.RequestVersion);
        Assert.Equal("KEY", latest.Patch!.CdKey);
        Assert.Equal(30, latest.Patch.Size);
        Assert.Equal(2, latest.Patch.Patches!.Count);
        Assert.Equal("https://example.invalid/patch.json", latest.Patch.PatchInfoUrl);
        Assert.Equal("1.7.0", latest.PrePatch!.Version);
        Assert.Null(latest.PrePatch.CdKey);
    }


    [Fact]
    public void ParsePatchInfo_ReadsTheThreeKindsOfFiles()
    {
        GryphlinePatchInfo info = GryphlinePatchPlanner.ParsePatchInfo(Encoding.UTF8.GetBytes(PatchInfoJson));

        Assert.Equal(3, info.Files!.Count);
        Assert.Equal("AA/OLD1.chk", info.Files[0].Patch![0].BaseFile);
        Assert.Equal(200, info.Files[0].Patch![0].BaseSize);
        Assert.NotNull(info.Files[1].LocalPath);
        Assert.Null(info.Files[2].Patch);
        Assert.Null(info.Files[2].LocalPath);
        Assert.Equal("Endfield_Data/StreamingAssets/VFS/AA/NEW1.chk", GryphlinePatchPlanner.ToGamePath(info, "AA/NEW1.chk"));
    }


    [Fact]
    public void ScoreLocalMatch_CountsDiffBasesAndUnchangedFilesBySize()
    {
        GryphlinePatchInfo info = GryphlinePatchPlanner.ParsePatchInfo(Encoding.UTF8.GetBytes(PatchInfoJson));
        var local = new Dictionary<string, long> { ["AA/OLD1.chk"] = 200, ["BB/SAME.chk"] = 1000 };

        Assert.Equal(1.0, GryphlinePatchPlanner.ScoreLocalMatch(info, x => local.TryGetValue(x, out long v) ? v : null));

        // 旧文件大小不对：一半对上
        local["AA/OLD1.chk"] = 201;
        Assert.Equal(0.5, GryphlinePatchPlanner.ScoreLocalMatch(info, x => local.TryGetValue(x, out long v) ? v : null));

        // 新文件（local_path）不算：本机本来就没有
        Assert.Equal(0, GryphlinePatchPlanner.ScoreLocalMatch(info, _ => null));
    }


    [Fact]
    public void GetCandidateLocalVersions_ListsOlderPatchVersionsNewestFirst()
    {
        IReadOnlyList<string> list = GryphlinePatchPlanner.GetCandidateLocalVersions("1.6.3");

        Assert.Equal(["1.6.2", "1.6.1", "1.6.0"], list.Take(3));
        Assert.Contains("1.5.3", list);
        Assert.Contains("1.5.9", list);
        Assert.DoesNotContain("1.6.3", list);
        Assert.DoesNotContain("1.4.9", list);
        Assert.Empty(GryphlinePatchPlanner.GetCandidateLocalVersions("abc"));
    }


    [Fact]
    public void GetZipPath_MapsGamePathsToZipEntries()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Endfield.exe",
            "vfs_files/files/Endfield_Data/StreamingAssets/VFS/AA/AA.blc",
        };

        Assert.Equal("Endfield.exe", GryphlinePatchPlanner.GetZipPath("Endfield.exe", names));
        Assert.Equal("vfs_files/files/Endfield_Data/StreamingAssets/VFS/AA/AA.blc", GryphlinePatchPlanner.GetZipPath("Endfield_Data/StreamingAssets/VFS/AA/AA.blc", names));
        Assert.Null(GryphlinePatchPlanner.GetZipPath("missing.dll", names));
    }


    [Fact]
    public void ParseDeleteList_SkipsBlankLinesAndNormalizesSlashes()
    {
        IReadOnlyList<string> list = GryphlinePatchPlanner.ParseDeleteList("﻿Endfield_Data/StreamingAssets/VFS/AA/OLD1.chk\r\n\r\nEndfield_Data\\StreamingAssets\\VFS\\BB\\X.chk\n");

        Assert.Equal(["Endfield_Data/StreamingAssets/VFS/AA/OLD1.chk", "Endfield_Data/StreamingAssets/VFS/BB/X.chk"], list);
    }


    [Fact]
    public void PredownloadMarker_IsFoundByTargetVersion()
    {
        string root = Path.Combine(Path.GetTempPath(), "starward-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = GryphlinePatchPlanner.GetStagingDirectory(root, "1.5.3", "1.6.3");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, GryphlinePatchPlanner.PredownloadMarkerFileName),
                              GryphlinePatchPlanner.SerializeMarker(new GryphlinePredownloadMarker { LocalVersion = "1.5.3", TargetVersion = "1.6.3" }));

            Assert.True(GryphlinePatchPlanner.IsPredownloadFinished(root, "1.5.3", "1.6.3"));
            Assert.False(GryphlinePatchPlanner.IsPredownloadFinished(root, "1.5.2", "1.6.3"));
            Assert.Equal("1.5.3", GryphlinePatchPlanner.FindMarker(root, "1.6.3")!.LocalVersion);
            Assert.Null(GryphlinePatchPlanner.FindMarker(root, "1.7.0"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }


    [Fact]
    public void Journal_RoundTrips()
    {
        string json = GryphlinePatchPlanner.SerializeJournal(["a", "B"]);
        IReadOnlySet<string> set = GryphlinePatchPlanner.ParseJournal(json);

        Assert.True(set.Contains("A"));
        Assert.True(set.Contains("b"));
        Assert.Empty(GryphlinePatchPlanner.ParseJournal("not json"));
    }

}
