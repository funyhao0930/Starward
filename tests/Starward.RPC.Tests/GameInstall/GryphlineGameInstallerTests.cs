using Microsoft.Extensions.Logging.Abstractions;
using Starward.Core.Games.Gryphline;
using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Gryphline;
using Starward.RPC.GameInstall;
using Starward.RPC.GameInstall.Gryphline;
using Starward.RPC.Tests.Fakes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Starward.RPC.Tests.GameInstall;

/// <summary>
/// 终末地的补丁更新与预下载，对着记忆体里的假接口与假 CDN 跑。
/// <para/>
/// 补丁包照线上 1.5.3 → 1.6.3 的布局做：WinZip AES-256 加密、切成两卷，
/// 里面有 VFS 差分（HDIFFSF20）、新的 VFS 文件、变了的 exe、删除清单与新的 game_files / config.ini。
/// 本机版本 1.0.0，线上 1.1.0。
/// </summary>
public sealed class GryphlineGameInstallerTests : IDisposable
{

    private const string Api = "https://launcher.gryphline.com/api/proxy/batch_proxy";

    private const string PatchBase = "https://cdn.test/patch/v2/x_1_1_0_1_0_0.zip.";

    private const string PatchInfoUrl = "https://cdn.test/patch/1.0.0/v2/patch.json";

    private const string FilePath = "https://cdn.test/1.1.0/files";

    private const string CdKey = "TestCdKey000000000000000000000000";

    private const string Vfs = "Endfield_Data/StreamingAssets/VFS/";

    /// <summary>
    /// 没变的文件的个数。本机版本靠这些与差分的旧文件的大小来认，个数太少时缺一个就低于门槛
    /// </summary>
    private const int UnchangedCount = 10;


    private readonly string _root = Path.Combine(Path.GetTempPath(), "StarwardTests", Guid.NewGuid().ToString("N"));

    private readonly FakeCdn _cdn = new();

    private readonly byte[] _old1 = RandomBytes(3000, 1);

    private readonly byte[] _new1;

    private readonly List<byte[]> _parts;

    private readonly byte[] _patchInfo;

    private readonly byte[] _fullPackage;

    private const string FullPackageUrl = "https://cdn.test/1.1.0/packs/full.zip.001";

    /// <summary>
    /// 1.1.0 已经上线：接口给 patch 与 cd_key；否则还在预下载期，只给 pre_patch
    /// </summary>
    private bool _released = true;


    public GryphlineGameInstallerTests()
    {
        _new1 = [.. _old1, .. RandomBytes(500, 2)];
        _new1[10] ^= 0x55;
        _new1[2999] += 3;

        _patchInfo = Encoding.UTF8.GetBytes(new JsonObject
        {
            ["version"] = "1.1.0",
            ["vfs_base_path"] = "Endfield_Data/StreamingAssets/VFS",
            ["files"] = new JsonArray(
            [
                new JsonObject
                {
                    ["name"] = "AA/NEW1.chk",
                    ["md5"] = Md5(_new1),
                    ["size"] = _new1.Length,
                    ["diffType"] = 1,
                    ["patch"] = new JsonArray(new JsonObject
                    {
                        ["base_file"] = "AA/OLD1.chk",
                        ["base_md5"] = Md5(_old1),
                        ["base_size"] = _old1.Length,
                        ["patch"] = "diff_1.1.0_1.0.0/AA_NEW1.chk_patch",
                        ["patch_size"] = 1,
                    }),
                },
                new JsonObject
                {
                    ["name"] = "AA/AA.blc",
                    ["md5"] = Md5("blc 1.1.0"),
                    ["size"] = 9,
                    ["diffType"] = 2,
                    ["local_path"] = "vfs_files/files/" + Vfs + "AA/AA.blc",
                },
                new JsonObject
                {
                    ["name"] = "BB/SAME.chk",
                    ["md5"] = Md5("same file"),
                    ["size"] = 9,
                    ["diffType"] = 1,
                },
                .. Enumerable.Range(0, UnchangedCount).Select(i => (JsonNode?)new JsonObject
                {
                    ["name"] = $"DD/U{i}.chk",
                    ["md5"] = Md5($"unchanged {i}"),
                    ["size"] = Bytes($"unchanged {i}").Length,
                    ["diffType"] = 1,
                }),
            ]),
        }.ToJsonString());

        JsonObject Verify(string path, byte[] content) => new() { ["path"] = path, ["md5"] = Md5(content), ["size"] = content.Length };
        byte[] verifyFiles = Encoding.UTF8.GetBytes(new JsonObject
        {
            ["move"] = new JsonArray(
                Verify("Endfield.exe", Bytes("exe 1.1.0")),
                Verify(Vfs + "AA/AA.blc", Bytes("blc 1.1.0")),
                Verify("game_files", Bytes("files 1.1.0")),
                Verify("config.ini", Bytes("config 1.1.0"))),
            ["patch"] = new JsonArray(Verify(Vfs + "AA/NEW1.chk", _new1)),
        }.ToJsonString());

        byte[] zip = FakePatchZip.Build(
        [
            ("Endfield.exe", Bytes("exe 1.1.0")),
            ("vfs_files/files/" + Vfs + "AA/AA.blc", Bytes("blc 1.1.0")),
            ("vfs_files/vfs_patch/diff_1.1.0_1.0.0/AA_NEW1.chk_patch", FakePatchZip.Diff(_old1, _new1)),
            ("game_files", Bytes("files 1.1.0")),
            ("config.ini", Bytes("config 1.1.0")),
            ("patch.json", _patchInfo),
            ("verify_files.json", verifyFiles),
            ("delete_files.txt", Bytes(Vfs + "AA/OLD1.chk\r\n" + Vfs + "CC/GONE.chk\r\n")),
        ], CdKey);
        // 切在 zip 中间，条目会跨卷
        _parts = FakePatchZip.Split(zip, zip.Length / 2 + 1);
        for (int i = 0; i < _parts.Count; i++)
        {
            _cdn.Add(PartUrl(i), _parts[i]);
        }
        _cdn.Add(PatchInfoUrl, _patchInfo);
        // 1.1.0 的完整文件：差分打不上时单独下载，按文件更新时全部比对；整包只用来读中央目录
        var full = new List<(string Path, byte[] Content)>
        {
            ("Endfield.exe", Bytes("exe 1.1.0")),
            (Vfs + "AA/NEW1.chk", _new1),
            (Vfs + "AA/AA.blc", Bytes("blc 1.1.0")),
            (Vfs + "BB/SAME.chk", Bytes("same file")),
            ("game_files", Bytes("files 1.1.0")),
            ("config.ini", Bytes("config 1.1.0")),
        };
        full.AddRange(Enumerable.Range(0, UnchangedCount).Select(i => (Vfs + $"DD/U{i}.chk", Bytes($"unchanged {i}"))));
        foreach ((string path, byte[] content) in full)
        {
            _cdn.Add(FilePath + "/" + path, content);
        }
        _fullPackage = BuildFullPackage(full);
        _cdn.Add(FullPackageUrl, _fullPackage);
        _cdn.Responder = Respond;

        WriteGame("Endfield.exe", Bytes("exe 1.0.0"));
        WriteGame("game_files", Bytes("files 1.0.0"));
        WriteGame("config.ini", Bytes("config 1.0.0"));
        WriteGame(Vfs + "AA/OLD1.chk", _old1);
        WriteGame(Vfs + "BB/SAME.chk", Bytes("same file"));
        WriteGame(Vfs + "CC/GONE.chk", Bytes("gone"));
        for (int i = 0; i < UnchangedCount; i++)
        {
            WriteGame(Vfs + $"DD/U{i}.chk", Bytes($"unchanged {i}"));
        }
    }


    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }



    #region Helpers


    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Md5(byte[] content) => Convert.ToHexStringLower(MD5.HashData(content));

    private static string Md5(string content) => Md5(Bytes(content));

    private static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    /// <summary>
    /// 不加密的整包，与官方整包一样是普通 zip（这里只有一卷）
    /// </summary>
    private static byte[] BuildFullPackage(IEnumerable<(string Path, byte[] Content)> files)
    {
        var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            foreach ((string path, byte[] content) in files)
            {
                using Stream entry = zip.CreateEntry(path, System.IO.Compression.CompressionLevel.NoCompression).Open();
                entry.Write(content);
            }
        }
        return ms.ToArray();
    }

    private static string PartUrl(int index) => PatchBase + (index + 1).ToString("D3");

    private string GameDir => GryphlineGameInstaller.GetGameDirectory(_root);

    private string PathOf(string relative) => Path.Combine(GameDir, relative.Replace('/', Path.DirectorySeparatorChar));

    private void WriteGame(string relative, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf(relative))!);
        File.WriteAllBytes(PathOf(relative), content);
    }

    private string ReadText(string relative) => File.ReadAllText(PathOf(relative));

    private string StagingRoot => Path.Combine(_root, GryphlinePatchPlanner.StagingFolderName);


    private JsonArray Parts() => new(_parts.Select((x, i) => (JsonNode?)new JsonObject
    {
        ["url"] = PartUrl(i),
        ["md5"] = Md5(x),
        ["package_size"] = x.Length.ToString(),
    }).ToArray());


    /// <summary>
    /// 假的 get_latest_game：按请求里的本机版本号回答
    /// </summary>
    private byte[]? Respond(string url, string body)
    {
        if (url != Api)
        {
            return null;
        }
        string version = (string?)JsonNode.Parse(body)!["proxy_reqs"]![0]!["get_latest_game_req"]!["version"] ?? "";
        string latest = _released ? "1.1.0" : "1.0.0";
        var response = new JsonObject
        {
            ["action"] = version == latest ? 0 : 1,
            ["version"] = latest,
            ["request_version"] = version,
            ["pkg"] = new JsonObject
            {
                ["packs"] = _released
                          ? new JsonArray(new JsonObject { ["url"] = FullPackageUrl, ["md5"] = Md5(_fullPackage), ["package_size"] = _fullPackage.Length.ToString() })
                          : new JsonArray(),
                ["file_path"] = _released ? FilePath : "https://cdn.test/1.0.0/files",
                ["game_files_md5"] = Md5(_released ? "files 1.1.0" : "files 1.0.0"),
            },
        };
        if (version == "1.0.0" && _released)
        {
            response["patch"] = new JsonObject
            {
                ["patches"] = Parts(),
                ["package_size"] = _parts.Sum(x => x.Length).ToString(),
                ["v2_patch_info_url"] = PatchInfoUrl,
                ["v2_patch_info_md5"] = Md5(_patchInfo),
                ["cd_key"] = CdKey,
            };
        }
        if (version == "1.0.0" && !_released)
        {
            response["pre_patch"] = new JsonObject
            {
                ["version"] = "1.1.0",
                ["patches"] = Parts(),
                ["package_size"] = _parts.Sum(x => x.Length).ToString(),
            };
        }
        return Bytes(new JsonObject
        {
            ["proxy_rsps"] = new JsonArray(new JsonObject { ["kind"] = "get_latest_game", ["get_latest_game_rsp"] = response }),
        }.ToJsonString());
    }


    private async Task<GameInstallContext> RunAsync(GameInstallOperation operation)
    {
        var helper = new GameInstallHelper(NullLogger<GameInstallHelper>.Instance, _cdn);
        var installer = new GryphlineGameInstaller(NullLogger<GryphlineGameInstaller>.Instance,
                                                   new GryphlineLauncherClient(new HttpClient(_cdn, disposeHandler: false)),
                                                   helper);
        var context = new GameInstallContext
        {
            GameId = new GameId { GameBiz = "gryphline:endfield", Id = "gryphline:endfield" },
            InstallPath = _root,
            Operation = operation,
        };
        await installer.ExecuteAsync(context, GryphlineGameMapping.EndfieldDefault, TestContext.Current.CancellationToken);
        return context;
    }


    private void AssertUpdated()
    {
        Assert.Equal("exe 1.1.0", ReadText("Endfield.exe"));
        Assert.Equal(_new1, File.ReadAllBytes(PathOf(Vfs + "AA/NEW1.chk")));
        Assert.Equal("blc 1.1.0", ReadText(Vfs + "AA/AA.blc"));
        Assert.Equal("same file", ReadText(Vfs + "BB/SAME.chk"));
        Assert.Equal("files 1.1.0", ReadText("game_files"));
        Assert.Equal("config 1.1.0", ReadText("config.ini"));
        Assert.False(File.Exists(PathOf(Vfs + "AA/OLD1.chk")));
        Assert.False(File.Exists(PathOf(Vfs + "CC/GONE.chk")));
        Assert.Empty(Directory.EnumerateFiles(GameDir, "*_tmp", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(StagingRoot));
    }


    #endregion



    [Fact]
    public async Task Update_AppliesThePatch()
    {
        GameInstallContext context = await RunAsync(GameInstallOperation.Update);

        AssertUpdated();
        Assert.Equal("1.0.0", context.LocalGameVersion);
        Assert.Equal(GameInstallDownloadMode.Patch, context.DownloadMode);
        // 都从补丁来，不必单独下载完整文件
        Assert.False(_cdn.WasRequested(FilePath));
    }


    [Fact]
    public async Task Update_DownloadsTheFullFile_WhenTheDiffBaseWasModified()
    {
        // 大小没变、内容被改过：差分打得出来但 MD5 不对，改为下载完整的新文件
        byte[] modified = (byte[])_old1.Clone();
        modified[100] ^= 1;
        WriteGame(Vfs + "AA/OLD1.chk", modified);

        await RunAsync(GameInstallOperation.Update);

        AssertUpdated();
        Assert.True(_cdn.WasRequested(FilePath + "/" + Vfs + "AA/NEW1.chk"));
    }


    [Fact]
    public async Task Update_RestoresMissingUnchangedFiles()
    {
        File.Delete(PathOf(Vfs + "BB/SAME.chk"));

        await RunAsync(GameInstallOperation.Update);

        AssertUpdated();
        Assert.True(_cdn.WasRequested(FilePath + "/" + Vfs + "BB/SAME.chk"));
    }


    [Fact]
    public async Task Update_FallsBackToFileSync_WhenLocalFilesDoNotMatchThePatch()
    {
        // 本机不像 1.0.0：不打补丁，改走按文件比对，照样更新到 1.1.0
        File.Delete(PathOf(Vfs + "AA/OLD1.chk"));
        File.Delete(PathOf(Vfs + "BB/SAME.chk"));
        for (int i = 0; i < UnchangedCount; i++)
        {
            File.Delete(PathOf(Vfs + $"DD/U{i}.chk"));
        }

        GameInstallContext context = await RunAsync(GameInstallOperation.Update);

        AssertUpdated();
        Assert.Equal(GameInstallDownloadMode.SingleFile, context.DownloadMode);
        Assert.False(_cdn.WasRequested(PartUrl(0)));
    }


    [Fact]
    public async Task Predownload_DownloadsPartsOnly_ThenUpdateReusesThem()
    {
        _released = false;

        GameInstallContext predownload = await RunAsync(GameInstallOperation.Predownload);

        Assert.Equal("1.1.0", predownload.PredownloadVersion);
        Assert.True(GryphlinePatchPlanner.IsPredownloadFinished(_root, "1.0.0", "1.1.0"));
        // 预下载不动游戏目录
        Assert.Equal("exe 1.0.0", ReadText("Endfield.exe"));
        Assert.Equal("files 1.0.0", ReadText("game_files"));
        Assert.Equal(1, _cdn.CountRequests(PartUrl(0)));

        // 正式上线：接口给出 cd_key，分卷还是同一批，只校验不重新下载
        _released = true;
        GameInstallContext update = await RunAsync(GameInstallOperation.Update);

        AssertUpdated();
        Assert.Equal("1.0.0", update.LocalGameVersion);
        Assert.Equal(1, _cdn.CountRequests(PartUrl(0)));
        Assert.Equal(1, _cdn.CountRequests(PartUrl(1)));
    }


    [Fact]
    public async Task Predownload_RequiresAnUpToDateGame()
    {
        _released = false;
        WriteGame("game_files", Bytes("files 0.9.0"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(GameInstallOperation.Predownload));
        Assert.False(_cdn.WasRequested(PartUrl(0)));
    }

}
