using Microsoft.Extensions.Logging.Abstractions;
using Starward.Core.Games.Kuro;
using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Kuro;
using Starward.RPC.GameInstall;
using Starward.RPC.GameInstall.Kuro;
using Starward.RPC.Tests.Fakes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Starward.RPC.Tests.GameInstall;

/// <summary>
/// 鸣潮安装器的整个流程，对着记忆体里的假 CDN 跑。
/// <para/>
/// 配置的形状照线上 3.7.0：旧版配置是一个整包（= 共用 + HD），分级配置是 common / hd / sd / uhd 四个资源包，
/// 各档专属的 pak 在 Client/Content/{UHD|HD|SD}。文件只有几个字节，重点是下载了哪些、删了哪些、走了哪一份配置。
/// krpdiff 差分要真的 HDiff 数据，这里不测，补丁只测没有差分的热更新。
/// </summary>
public sealed class KuroGameInstallerTests : IDisposable
{

    private const string LegacyIndexUrl = "https://prod-alicdn-gamestarter.kurogame.com/launcher/game/G153/50004_obOHXFrFanqsaIEOmuKroCcbZkQRBC7c/index.json";

    private const string TieredIndexUrl = "https://prod-alicdn-gamestarter.kurogame.com/launcher/game/50004_P7xcUZnEr1AXIGON25E6KjpOgTlVrg6e/G153/official/index.json";

    private const string Cdn = "https://cdn.test/";


    private const string Exe = "Wuthering Waves.exe";

    private const string CommonPak = "Client/Content/Paks/pakchunk0-WindowsNoEditor.pak";

    private const string HdPak = "Client/Content/HD/pakchunk1-HD-WindowsNoEditor.pak";

    private const string SdPak = "Client/Content/SD/pakchunk1-SD-WindowsNoEditor.pak";

    private const string UhdPak = "Client/Content/UHD/pakchunk1-UHD-WindowsNoEditor.pak";


    private readonly string _root = Path.Combine(Path.GetTempPath(), "StarwardTests", Guid.NewGuid().ToString("N"));

    private readonly FakeCdn _cdn = new();


    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }



    #region Helpers


    private string GameDir => Path.Combine(_root, KuroGameMapping.GameFolderName);

    private string PathOf(string dest) => Path.Combine(GameDir, dest.Replace('/', Path.DirectorySeparatorChar));

    private string Read(string dest) => File.ReadAllText(PathOf(dest));

    private bool TierFolderExists(string dest) => Directory.Exists(Path.GetDirectoryName(PathOf(dest)));

    private string? LocalVersion() => (string?)JsonNode.Parse(File.ReadAllText(Path.Combine(GameDir, KuroGameMapping.VersionFileName)))!["version"];


    /// <summary>
    /// 某个版本各资源包的文件，内容带版本号，换版本就变了
    /// </summary>
    private static Dictionary<string, (string Dest, string Content)[]> Packs(string version) => new()
    {
        ["common"] = [(Exe, $"exe {version}"), (CommonPak, $"common {version}")],
        ["hd"] = [(HdPak, $"hd {version}")],
        ["sd"] = [(SdPak, $"sd {version}")],
        ["uhd"] = [(UhdPak, $"uhd {version}")],
    };


    /// <summary>
    /// 没有差分、只有普通文件的补丁（官方叫 hotFix）
    /// </summary>
    private sealed record PlainPatch(string FromVersion, (string Dest, string Content)[] Files, string[] DeleteFiles);


    private static string Md5(string content) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(content)));

    private static long Size(string content) => Encoding.UTF8.GetByteCount(content);


    private static JsonObject FileEntry(string dest, string content, string? fromFolder = null)
    {
        var entry = new JsonObject { ["dest"] = dest, ["md5"] = Md5(content), ["size"] = Size(content) };
        if (fromFolder is not null)
        {
            entry["fromFolder"] = fromFolder;
        }
        return entry;
    }


    private void AddJson(string url, JsonNode node) => _cdn.Add(url, Encoding.UTF8.GetBytes(node.ToJsonString()));


    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());


    private static JsonArray CdnList() => new(new JsonObject { ["url"] = Cdn, ["P"] = 1 });


    private static JsonObject Experiment() => new()
    {
        ["repair"] = new JsonObject { ["directoryIntegrityCheckList"] = """[{"dir":"Client/Content/Paks","exts":["pak","sig"],"recursive":true}]""" },
    };


    /// <summary>
    /// 发布一个资源包：完整清单、清单里的文件，以及（有的话）从旧版本来的补丁
    /// </summary>
    private JsonObject PackConfig(string source, string version, string pack, (string Dest, string Content)[] files, PlainPatch? patch)
    {
        string folder = $"{source}/{version}/zip/";
        var resource = new JsonArray();
        foreach ((string dest, string content) in files)
        {
            resource.Add(FileEntry(dest, content));
            _cdn.Add(Cdn + folder + dest, Encoding.UTF8.GetBytes(content));
        }
        string indexFile = $"{source}/{version}/{pack}/indexFile.json";
        AddJson(Cdn + indexFile, new JsonObject { ["resource"] = resource });

        var patchConfig = new JsonArray();
        if (patch is not null)
        {
            var patchResource = new JsonArray();
            foreach ((string dest, string content) in patch.Files)
            {
                // 热更新文件照官方的写法用 fromFolder 指到完整文件的目录
                patchResource.Add(FileEntry(dest, content, folder));
            }
            string patchIndex = $"{source}/{version}/{pack}/{patch.FromVersion}/indexFile.json";
            AddJson(Cdn + patchIndex, new JsonObject
            {
                ["resource"] = patchResource,
                ["deleteFiles"] = Strings(patch.DeleteFiles),
                ["applyTypes"] = Strings(["group"]),
            });
            patchConfig.Add(new JsonObject
            {
                ["version"] = patch.FromVersion,
                ["indexFile"] = patchIndex,
                ["baseUrl"] = $"{source}/{version}/{pack}/{patch.FromVersion}/resources",
                ["size"] = patch.Files.Sum(x => Size(x.Content)),
            });
        }
        long size = files.Sum(x => Size(x.Content));
        return new JsonObject
        {
            ["version"] = version,
            ["indexFile"] = indexFile,
            ["baseUrl"] = folder,
            ["size"] = size,
            ["unCompressSize"] = size,
            ["patchConfig"] = patchConfig,
        };
    }


    /// <summary>
    /// 新启动器的分级配置
    /// </summary>
    private void PublishTiered(string version, Dictionary<string, PlainPatch>? patches = null, string? predownloadVersion = null, Dictionary<string, PlainPatch>? predownloadPatches = null)
    {
        var resourcePacks = new JsonObject();
        foreach ((string name, (string Dest, string Content)[] files) in Packs(version))
        {
            resourcePacks[name] = PackConfig("tiered", version, name, files, patches?.GetValueOrDefault(name));
        }
        JsonObject? predownload = null;
        if (predownloadVersion is not null)
        {
            // 线上还没见过分级配置的预下载，照安装器的假设：与顶层同形，没有 bundles 与 cdnList 时沿用顶层的
            var predownloadPacks = new JsonObject();
            foreach ((string name, (string Dest, string Content)[] files) in Packs(predownloadVersion))
            {
                predownloadPacks[name] = PackConfig("tiered", predownloadVersion, name, files, predownloadPatches?.GetValueOrDefault(name));
            }
            predownload = new JsonObject { ["resourcePacks"] = predownloadPacks };
        }
        AddJson(TieredIndexUrl, new JsonObject
        {
            ["cdnList"] = CdnList(),
            ["resourcePacks"] = resourcePacks,
            ["bundles"] = new JsonObject
            {
                ["HD"] = new JsonObject { ["resourcePacks"] = Strings(["common", "hd"]) },
                ["SD"] = new JsonObject { ["resourcePacks"] = Strings(["common", "sd"]) },
                ["UHD"] = new JsonObject { ["resourcePacks"] = Strings(["common", "uhd"]) },
            },
            ["config"] = new JsonObject { ["predownloadSwitch"] = 1, ["experiment"] = Experiment() },
            ["predownload"] = predownload,
        });
    }


    /// <summary>
    /// 旧启动器的配置：一个整包 = 共用 + HD，与线上一样
    /// </summary>
    private void PublishLegacy(string version, PlainPatch? patch = null, string? predownloadVersion = null, PlainPatch? predownloadPatch = null)
    {
        var index = new JsonObject
        {
            ["default"] = LegacyResource(version, patch, withCdn: true),
            ["predownloadSwitch"] = predownloadVersion is null ? 0 : 1,
            ["experiment"] = Experiment(),
        };
        if (predownloadVersion is not null)
        {
            // 线上的预下载没有自己的 cdnList
            index["predownload"] = LegacyResource(predownloadVersion, predownloadPatch, withCdn: false);
        }
        AddJson(LegacyIndexUrl, index);
    }


    private JsonObject LegacyResource(string version, PlainPatch? patch, bool withCdn)
    {
        Dictionary<string, (string Dest, string Content)[]> packs = Packs(version);
        var resource = new JsonObject
        {
            ["version"] = version,
            ["config"] = PackConfig("legacy", version, "full", [.. packs["common"], .. packs["hd"]], patch),
        };
        if (withCdn)
        {
            resource["cdnList"] = CdnList();
        }
        return resource;
    }


    private async Task<GameInstallContext> RunAsync(GameInstallOperation operation, string? tiers = null, CancellationToken? cancellationToken = null)
    {
        var helper = new GameInstallHelper(NullLogger<GameInstallHelper>.Instance, _cdn);
        var installer = new KuroGameInstaller(NullLogger<KuroGameInstaller>.Instance,
                                              new KuroLauncherClient(new HttpClient(_cdn, disposeHandler: false)),
                                              helper,
                                              new KuroDirDiffPatcher(NullLogger<KuroDirDiffPatcher>.Instance, helper));
        var context = new GameInstallContext
        {
            GameId = new GameId { GameBiz = "kuro:wuwa:global", Id = "kuro:wuwa:global" },
            InstallPath = _root,
            Operation = operation,
            ResourceTiers = tiers,
        };
        await installer.ExecuteAsync(context, KuroGameMapping.WutheringWavesGlobal, cancellationToken ?? TestContext.Current.CancellationToken);
        return context;
    }


    #endregion



    #region Install


    [Fact]
    public async Task Install_WithSd_DownloadsTheSharedAndSdPacksOnly()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");

        await RunAsync(GameInstallOperation.Install, "sd");

        Assert.Equal("exe 3.7.0", Read(Exe));
        Assert.Equal("common 3.7.0", Read(CommonPak));
        Assert.Equal("sd 3.7.0", Read(SdPak));
        Assert.False(TierFolderExists(HdPak));
        Assert.Equal([KuroResourceTier.SD], KuroResourceTier.GetInstalledTiers(GameDir));
        Assert.Equal("3.7.0", LocalVersion());
    }


    [Fact]
    public async Task Install_SeveralTiersAtOnce()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");

        await RunAsync(GameInstallOperation.Install, "uhd,sd");

        Assert.Equal([KuroResourceTier.UHD, KuroResourceTier.SD], KuroResourceTier.GetInstalledTiers(GameDir));
        Assert.Equal("uhd 3.7.0", Read(UhdPak));
    }


    /// <summary>
    /// 只要 HD 时照旧走旧版配置，连分级配置的清单都不读
    /// </summary>
    [Fact]
    public async Task Install_WithoutTiers_KeepsUsingTheLegacyIndex()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");

        await RunAsync(GameInstallOperation.Install);

        Assert.Equal("hd 3.7.0", Read(HdPak));
        Assert.Equal("exe 3.7.0", Read(Exe));
        Assert.True(_cdn.WasRequested("legacy/3.7.0/full/indexFile.json"));
        Assert.False(_cdn.WasRequested("tiered/3.7.0/"));
    }


    /// <summary>
    /// 在已是最新版本的游戏上加装一档：原有的文件只看大小，不再下载，也不再逐个算 MD5
    /// （否则加装之前要先把 40 GB 的共用文件整个读一遍）。内容坏了是修复的事。
    /// </summary>
    [Fact]
    public async Task Install_AddsATierWithoutDownloadingWhatIsAlreadyThere()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install);
        // 大小相同、内容不同：只看大小就认不出来
        File.WriteAllText(PathOf(CommonPak), "common 3.7.X");
        _cdn.Requests.Clear();

        await RunAsync(GameInstallOperation.Install, "hd,sd");

        Assert.Equal("sd 3.7.0", Read(SdPak));
        Assert.Equal("hd 3.7.0", Read(HdPak));
        Assert.True(_cdn.WasRequested("tiered/3.7.0/zip/" + SdPak));
        Assert.False(_cdn.WasRequested("zip/" + CommonPak));
        Assert.False(_cdn.WasRequested("zip/" + HdPak));
        Assert.False(_cdn.WasRequested("zip/" + Exe));
        Assert.Equal("common 3.7.X", Read(CommonPak));

        await RunAsync(GameInstallOperation.Repair);

        Assert.Equal("common 3.7.0", Read(CommonPak));
    }


    [Fact]
    public async Task Install_RemovesTiersThatAreNoLongerWanted()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install, "hd,sd");
        Assert.Equal([KuroResourceTier.HD, KuroResourceTier.SD], KuroResourceTier.GetInstalledTiers(GameDir));

        await RunAsync(GameInstallOperation.Install, "hd");

        Assert.Equal([KuroResourceTier.HD], KuroResourceTier.GetInstalledTiers(GameDir));
        Assert.False(TierFolderExists(SdPak));
        Assert.Equal("common 3.7.0", Read(CommonPak));
    }


    [Fact]
    public async Task Install_SwitchesFromHdToSd()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install);

        await RunAsync(GameInstallOperation.Install, "sd");

        Assert.Equal([KuroResourceTier.SD], KuroResourceTier.GetInstalledTiers(GameDir));
        Assert.Equal("sd 3.7.0", Read(SdPak));
    }


    /// <summary>
    /// 新的一档没装好之前不能删旧的，否则玩家两档都没得玩
    /// </summary>
    [Fact]
    public async Task Install_KeepsTheOldTierWhenTheNewOneIsInterrupted()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _cdn.OnRequest = url =>
        {
            if (url.EndsWith(SdPak, StringComparison.Ordinal))
            {
                cts.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(GameInstallOperation.Install, "sd", cts.Token));

        Assert.Equal("hd 3.7.0", Read(HdPak));
        Assert.Contains(KuroResourceTier.HD, KuroResourceTier.GetInstalledTiers(GameDir));
    }


    /// <summary>
    /// 加装中断时已经下完的 pak 留在目录里，但那一档不能算装好：
    /// 否则启动页会列出它、带着它的参数启动，设置里也没法再加装一次
    /// </summary>
    [Fact]
    public async Task Install_AnInterruptedTierDoesNotCountAsInstalled()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _cdn.OnRequest = url =>
        {
            if (url.EndsWith(SdPak, StringComparison.Ordinal))
            {
                cts.Cancel();
            }
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(GameInstallOperation.Install, "hd,sd", cts.Token));
        // 当成这一档有个文件已经下完了，内容与大小相同的正确文件不一样
        File.WriteAllText(PathOf(SdPak), "sd 3.7.X");

        Assert.Equal([KuroResourceTier.HD], KuroResourceTier.GetInstalledTiers(GameDir));
        Assert.Equal([KuroResourceTier.SD], KuroResourceTier.GetIncompleteTiers(GameDir));

        // 再加装一次：这一档的文件不能只看大小就算数
        _cdn.OnRequest = null;
        await RunAsync(GameInstallOperation.Install, "hd,sd");

        Assert.Equal("sd 3.7.0", Read(SdPak));
        Assert.Equal([KuroResourceTier.HD, KuroResourceTier.SD], KuroResourceTier.GetInstalledTiers(GameDir));
        Assert.Empty(KuroResourceTier.GetIncompleteTiers(GameDir));
    }


    /// <summary>
    /// 全新安装中断后改按修复补完，那一档要算装好了，不能一直带着未完成标记
    /// </summary>
    [Fact]
    public async Task Repair_FinishesAnInterruptedFreshInstall()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _cdn.OnRequest = url =>
        {
            if (url.EndsWith(HdPak, StringComparison.Ordinal))
            {
                cts.Cancel();
            }
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(GameInstallOperation.Install, null, cts.Token));
        _cdn.OnRequest = null;
        Assert.Equal([KuroResourceTier.HD], KuroResourceTier.GetIncompleteTiers(GameDir));

        await RunAsync(GameInstallOperation.Repair);

        Assert.Equal("hd 3.7.0", Read(HdPak));
        Assert.Equal([KuroResourceTier.HD], KuroResourceTier.GetInstalledTiers(GameDir));
        Assert.Empty(KuroResourceTier.GetIncompleteTiers(GameDir));
    }


    /// <summary>
    /// 加装到一半、后来没有再选的那一档，变更分级时一起清掉，不留着占空间
    /// </summary>
    [Fact]
    public async Task Install_RemovesAnAbandonedTier()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _cdn.OnRequest = url =>
        {
            if (url.EndsWith(SdPak, StringComparison.Ordinal))
            {
                cts.Cancel();
            }
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(GameInstallOperation.Install, "hd,sd", cts.Token));
        _cdn.OnRequest = null;

        await RunAsync(GameInstallOperation.Install, "hd");

        Assert.False(TierFolderExists(SdPak));
        Assert.Equal("hd 3.7.0", Read(HdPak));
    }


    /// <summary>
    /// 删除分级是先改名再删；以前删到一半留下的目录，下次处理分级时清掉
    /// </summary>
    [Fact]
    public async Task Install_RemovesTiersByRenamingFirstAndCleansUpLeftovers()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install, "hd,sd");
        string leftover = Path.Combine(GameDir, "Client", "Content", "UHD" + KuroResourceTier.RemovingFolderSuffix);
        Directory.CreateDirectory(leftover);
        File.WriteAllText(Path.Combine(leftover, "pakchunk1-UHD-WindowsNoEditor.pak"), "");

        await RunAsync(GameInstallOperation.Install, "hd");

        Assert.False(TierFolderExists(SdPak));
        Assert.False(Directory.Exists(Path.GetDirectoryName(PathOf(SdPak)) + KuroResourceTier.RemovingFolderSuffix));
        Assert.False(Directory.Exists(leftover));
        Assert.Equal([KuroResourceTier.HD], KuroResourceTier.GetInstalledTiers(GameDir));
    }


    /// <summary>
    /// 本机不是目标版本时不能变更分级：照做就是不打补丁地整包重下，旧版本的 pak 也会留着。
    /// 界面按缓存的版本信息放行，新版本刚上线时就会走到这里。
    /// </summary>
    [Fact]
    public async Task Install_RefusesToChangeTiersWhenTheGameIsOutdated()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install);
        PublishLegacy("3.7.1");
        PublishTiered("3.7.1");
        _cdn.Requests.Clear();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(GameInstallOperation.Install, "hd,sd"));

        Assert.Contains("3.7.0", ex.Message, StringComparison.Ordinal);
        Assert.Contains("3.7.1", ex.Message, StringComparison.Ordinal);
        Assert.False(_cdn.WasRequested("zip/"));
        Assert.False(TierFolderExists(SdPak));
        Assert.Equal("3.7.0", LocalVersion());
    }


    /// <summary>
    /// 分级配置落后于旧版配置时，只装 HD 的已照旧版配置更新上去，这时加装一档会把共用文件盖回旧版本
    /// </summary>
    [Fact]
    public async Task Install_RefusesToAddATierFromAnOlderTieredIndex()
    {
        PublishLegacy("3.7.1");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install);
        Assert.Equal("3.7.1", LocalVersion());

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(GameInstallOperation.Install, "hd,sd"));

        Assert.Equal("common 3.7.1", Read(CommonPak));
        Assert.Equal("3.7.1", LocalVersion());
    }


    /// <summary>
    /// 要的分级只有分级配置给得了，而它读不到时要说清楚，不能照旧版配置装一份 HD 了事
    /// </summary>
    [Fact]
    public async Task Install_ExplainsWhenTheTieredIndexIsUnavailable()
    {
        PublishLegacy("3.7.0");

        NotSupportedException ex = await Assert.ThrowsAsync<NotSupportedException>(() => RunAsync(GameInstallOperation.Install, "sd"));

        Assert.Contains("SD", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(PathOf(Exe)));
    }


    /// <summary>
    /// 旧版配置读不到时，只要 HD 的安装改走分级配置
    /// </summary>
    [Fact]
    public async Task Install_FallsBackToTheTieredIndexWithoutTheLegacyOne()
    {
        PublishTiered("3.7.0");

        await RunAsync(GameInstallOperation.Install);

        Assert.Equal("hd 3.7.0", Read(HdPak));
        Assert.True(_cdn.WasRequested("tiered/3.7.0/common/indexFile.json"));
    }


    /// <summary>
    /// 官方新启动器可能在 launcherDownloadConfig.json 记了别的东西，不能整份覆盖
    /// </summary>
    [Fact]
    public async Task Install_KeepsFieldsOfTheLocalVersionFileItDoesNotKnow()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        Directory.CreateDirectory(GameDir);
        File.WriteAllText(Path.Combine(GameDir, KuroGameMapping.VersionFileName), """{"version":"3.6.1","appId":"50004","resourceLevel":"hd","isPreDownload":true}""");

        await RunAsync(GameInstallOperation.Install);

        JsonObject config = JsonNode.Parse(File.ReadAllText(Path.Combine(GameDir, KuroGameMapping.VersionFileName)))!.AsObject();
        Assert.Equal("3.7.0", (string?)config["version"]);
        Assert.Equal("hd", (string?)config["resourceLevel"]);
        Assert.Equal("50004", (string?)config["appId"]);
        Assert.False((bool)config["isPreDownload"]!);
    }


    #endregion



    #region Repair


    [Fact]
    public async Task Repair_FixesEveryInstalledTierAndDeletesStalePaks()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install, "hd,sd");
        File.WriteAllText(PathOf(SdPak), "broken");
        string stale = PathOf("Client/Content/Paks/pakchunk99-WindowsNoEditor.pak");
        File.WriteAllText(stale, "old");

        await RunAsync(GameInstallOperation.Repair);

        Assert.Equal("sd 3.7.0", Read(SdPak));
        Assert.Equal("hd 3.7.0", Read(HdPak));
        Assert.False(File.Exists(stale));
    }


    #endregion



    #region Update


    [Fact]
    public async Task Update_WithoutPatches_ComparesEveryInstalledPack()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install, "sd");
        PublishTiered("3.8.0");

        await RunAsync(GameInstallOperation.Update);

        Assert.Equal("exe 3.8.0", Read(Exe));
        Assert.Equal("common 3.8.0", Read(CommonPak));
        Assert.Equal("sd 3.8.0", Read(SdPak));
        Assert.False(TierFolderExists(HdPak));
        Assert.Equal("3.8.0", LocalVersion());
    }


    [Fact]
    public async Task Update_HdOnly_UsesTheLegacyIndexWhileItIsCurrent()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install);
        PublishLegacy("3.8.0");
        PublishTiered("3.8.0");
        _cdn.Requests.Clear();

        await RunAsync(GameInstallOperation.Update);

        Assert.Equal("hd 3.8.0", Read(HdPak));
        Assert.True(_cdn.WasRequested("legacy/3.8.0/full/indexFile.json"));
        Assert.False(_cdn.WasRequested("tiered/3.8.0/"));
    }


    /// <summary>
    /// 官方不再更新旧版配置时，只装 HD 的安装也要跟着分级配置更新，否则永远停在旧版本
    /// </summary>
    [Fact]
    public async Task Update_HdOnly_MovesToTheTieredIndexWhenTheLegacyOneFallsBehind()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install);
        PublishTiered("3.8.0");

        await RunAsync(GameInstallOperation.Update);

        Assert.Equal("hd 3.8.0", Read(HdPak));
        Assert.Equal("exe 3.8.0", Read(Exe));
        Assert.True(_cdn.WasRequested("tiered/3.8.0/common/indexFile.json"));
        Assert.Equal("3.8.0", LocalVersion());
    }


    /// <summary>
    /// 共用包有补丁、流畅包没有：前者打补丁，后者按完整清单比对，暂存目录最后清掉
    /// </summary>
    [Fact]
    public async Task Update_PatchesSomePacksAndComparesTheRest()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install, "sd");
        string removed = "Client/Binaries/Win64/old.dll";
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf(removed))!);
        File.WriteAllText(PathOf(removed), "x");
        PublishTiered("3.8.0", new() { ["common"] = new PlainPatch("3.7.0", [(Exe, "exe 3.8.0"), (CommonPak, "common 3.8.0")], [removed]) });

        GameInstallContext context = await RunAsync(GameInstallOperation.Update);

        Assert.Equal(GameInstallDownloadMode.Patch, context.DownloadMode);
        Assert.True(_cdn.WasRequested("tiered/3.8.0/common/3.7.0/indexFile.json"));
        Assert.Equal("exe 3.8.0", Read(Exe));
        Assert.Equal("common 3.8.0", Read(CommonPak));
        Assert.Equal("sd 3.8.0", Read(SdPak));
        Assert.False(File.Exists(PathOf(removed)));
        Assert.False(Directory.Exists(Path.Combine(_root, KuroDownloadPlanner.StagingFolderName)));
        Assert.Equal("3.8.0", LocalVersion());
    }


    #endregion



    #region Predownload


    /// <summary>
    /// 预下载只放暂存目录、不动游戏；正式更新时直接用暂存的文件，不再下载
    /// </summary>
    [Fact]
    public async Task Predownload_StagesThePatchAndTheUpdateReusesIt()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install);
        var patch = new PlainPatch("3.7.0", [(Exe, "exe 3.8.0"), (CommonPak, "common 3.8.0"), (HdPak, "hd 3.8.0")], []);
        PublishLegacy("3.7.0", predownloadVersion: "3.8.0", predownloadPatch: patch);

        await RunAsync(GameInstallOperation.Predownload);

        Assert.True(KuroDownloadPlanner.IsPredownloadFinished(_root, "3.7.0", "3.8.0"));
        Assert.Equal("hd 3.7.0", Read(HdPak));

        PublishLegacy("3.8.0", patch: patch);
        int downloads = _cdn.CountRequests("legacy/3.8.0/zip/" + HdPak);
        await RunAsync(GameInstallOperation.Update);

        Assert.Equal("hd 3.8.0", Read(HdPak));
        Assert.Equal("exe 3.8.0", Read(Exe));
        Assert.Equal(downloads, _cdn.CountRequests("legacy/3.8.0/zip/" + HdPak));
        Assert.Equal("3.8.0", LocalVersion());
    }


    /// <summary>
    /// 没有补丁的资源包，预下载先把大小变了的文件放进暂存目录，正式更新时收回来，不再下载。
    /// 否则预下载只拿到有补丁的那几个包，界面说完成了，正式更新还要整包下载。
    /// 版本号多一位，新旧文件的大小才不一样。
    /// </summary>
    [Fact]
    public async Task Predownload_StagesChangedFilesOfPacksWithoutAPatch()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install, "sd");
        var commonPatch = new Dictionary<string, PlainPatch>
        {
            ["common"] = new PlainPatch("3.7.0", [(Exe, "exe 3.10.0"), (CommonPak, "common 3.10.0")], []),
        };
        PublishTiered("3.7.0", predownloadVersion: "3.10.0", predownloadPatches: commonPatch);

        await RunAsync(GameInstallOperation.Predownload);

        Assert.True(_cdn.WasRequested("tiered/3.10.0/zip/" + SdPak));
        Assert.Equal("sd 3.7.0", Read(SdPak));
        Assert.True(KuroDownloadPlanner.IsPredownloadFinished(_root, "3.7.0", "3.10.0", KuroDownloadSource.Tiered, [KuroResourceTier.SD]));

        PublishTiered("3.10.0", patches: commonPatch);
        int downloads = _cdn.CountRequests("tiered/3.10.0/zip/" + SdPak);
        await RunAsync(GameInstallOperation.Update);

        Assert.Equal("sd 3.10.0", Read(SdPak));
        Assert.Equal("common 3.10.0", Read(CommonPak));
        Assert.Equal(downloads, _cdn.CountRequests("tiered/3.10.0/zip/" + SdPak));
        Assert.Equal("3.10.0", LocalVersion());
    }


    /// <summary>
    /// 预下载之后变更了分级，正式更新会走另一份配置或多一个资源包，暂存的东西不够，不能再说预下载完成了
    /// </summary>
    [Fact]
    public async Task Predownload_IsNoLongerFinishedAfterTheTiersChange()
    {
        PublishLegacy("3.7.0");
        PublishTiered("3.7.0");
        await RunAsync(GameInstallOperation.Install);
        var patch = new PlainPatch("3.7.0", [(Exe, "exe 3.8.0"), (CommonPak, "common 3.8.0"), (HdPak, "hd 3.8.0")], []);
        PublishLegacy("3.7.0", predownloadVersion: "3.8.0", predownloadPatch: patch);

        await RunAsync(GameInstallOperation.Predownload);

        Assert.True(KuroDownloadPlanner.IsPredownloadFinished(_root, "3.7.0", "3.8.0", KuroDownloadSource.Legacy, [KuroResourceTier.HD]));
        Assert.False(KuroDownloadPlanner.IsPredownloadFinished(_root, "3.7.0", "3.8.0", KuroDownloadSource.Tiered, [KuroResourceTier.HD, KuroResourceTier.SD]));
        Assert.False(KuroDownloadPlanner.IsPredownloadFinished(_root, "3.7.0", "3.8.0", KuroDownloadSource.Legacy, [KuroResourceTier.HD, KuroResourceTier.SD]));
    }


    #endregion


}
