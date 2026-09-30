using Starward.Core.Games;
using Starward.Core.Games.Kuro;
using Starward.Core.Tests.Games.Fakes;
using Xunit;

namespace Starward.Core.Tests.Games;

/// <summary>
/// 鸣潮 3.7.0 起的资源分级：看本机装了哪几档，决定启动参数 -krqlv 带哪一档。
/// <para/>
/// 目录结构取自 3.7.0 的官方清单：共用的 pak 在 Client/Content/Paks，
/// 各档专属的在 Client/Content/UHD|HD|SD，文件名形如 pakchunk1-HD-WindowsNoEditor.pak。
/// </summary>
public class KuroResourceTierTests : IDisposable
{

    private readonly string _root = Path.Combine(Path.GetTempPath(), "StarwardTests", Guid.NewGuid().ToString("N"));


    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }


    /// <summary>
    /// 官方启动器的安装根目录，游戏本体在其下的 Wuthering Waves Game
    /// </summary>
    private string GameDir => Path.Combine(_root, KuroGameMapping.GameFolderName);


    private void AddTier(string tier, bool withPak = true)
    {
        string dir = Path.Combine(GameDir, "Client", "Content", tier.ToUpperInvariant());
        Directory.CreateDirectory(dir);
        if (withPak)
        {
            string name = $"pakchunk1-{tier.ToUpperInvariant()}-WindowsNoEditor";
            File.WriteAllText(Path.Combine(dir, name + ".pak"), "");
            File.WriteAllText(Path.Combine(dir, name + ".sig"), "");
        }
    }



    [Fact]
    public void GetInstalledTiers_ListsTiersWithPaksFromBestToWorst()
    {
        AddTier(KuroResourceTier.SD);
        AddTier(KuroResourceTier.UHD);
        Directory.CreateDirectory(Path.Combine(GameDir, "Client", "Content", "Paks"));

        Assert.Equal([KuroResourceTier.UHD, KuroResourceTier.SD], KuroResourceTier.GetInstalledTiers(GameDir));
    }


    /// <summary>
    /// 切换或删除分级之后可能留下空目录，带着那一档启动会挂载不到资源
    /// </summary>
    [Fact]
    public void GetInstalledTiers_IgnoresEmptyTierFolders()
    {
        AddTier(KuroResourceTier.HD);
        AddTier(KuroResourceTier.SD, withPak: false);

        Assert.Equal([KuroResourceTier.HD], KuroResourceTier.GetInstalledTiers(GameDir));
    }


    [Fact]
    public void GetInstalledTiers_ReturnsEmptyForMissingOrUnknownFolders()
    {
        Assert.Empty(KuroResourceTier.GetInstalledTiers(GameDir));
        Assert.Empty(KuroResourceTier.GetInstalledTiers(null));
        Assert.Empty(KuroResourceTier.GetInstalledTiers(""));
    }



    [Theory]
    [InlineData("Client/Content/HD/pakchunk70-HD-WindowsNoEditor.pak", "hd")]
    [InlineData("Client/Content/UHD/pakchunk1-UHD-WindowsNoEditor.sig", "uhd")]
    [InlineData(@"Client\Content\SD\pakchunk1-SD-WindowsNoEditor.pak", "sd")]
    [InlineData("Client/Content/Paks/pakchunk0-WindowsNoEditor.pak", null)]
    [InlineData("Client/Content/Aki/Cursor/CursorHi.png", null)]
    [InlineData("Client/Content/HD", null)]
    [InlineData("Wuthering Waves.exe", null)]
    public void GetTierOfPath_OnlyMatchesFilesInsideTierFolders(string dest, string? tier)
    {
        Assert.Equal(tier, KuroResourceTier.GetTierOfPath(dest));
    }


    /// <summary>
    /// 公开的旧版清单只有 HD：共用文件加上 Client/Content/HD
    /// </summary>
    [Fact]
    public void GetTiersInIndex_FindsOnlyHdInThePublicIndex()
    {
        string[] dests =
        [
            "Wuthering Waves.exe",
            "Client/Content/Paks/pakchunk0-WindowsNoEditor.pak",
            "Client/Content/HD/pakchunk70-HD-WindowsNoEditor.pak",
            "Client/Content/HD/pakchunk70-HD-WindowsNoEditor.sig",
        ];
        Assert.Equal([KuroResourceTier.HD], KuroResourceTier.GetTiersInIndex(dests));
    }



    [Theory]
    [InlineData("-krqlv=hd", "hd")]
    [InlineData("-dx11 -krqlv=SD", "sd")]
    [InlineData("-krqlv=\"uhd\" -slno", "uhd")]
    [InlineData("-KRQLV=uhd", "uhd")]
    public void GetTierInArguments_ReadsTheWrittenTier(string arguments, string tier)
    {
        Assert.Equal(tier, KuroResourceTier.GetTierInArguments(arguments, out bool specified));
        Assert.True(specified);
    }


    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-dx11")]
    [InlineData("-xkrqlv=hd")]
    public void GetTierInArguments_IgnoresArgumentsWithoutIt(string? arguments)
    {
        Assert.Null(KuroResourceTier.GetTierInArguments(arguments, out bool specified));
        Assert.False(specified);
    }


    [Fact]
    public void GetTierInArguments_ReportsUnknownValuesAsSpecified()
    {
        Assert.Null(KuroResourceTier.GetTierInArguments("-krqlv=4k", out bool specified));
        Assert.True(specified);
    }


    [Theory]
    [InlineData("-krqlv=hd", null)]
    [InlineData("-krqlv=hd -dx11", "-dx11")]
    [InlineData("-dx11 -krqlv=hd", "-dx11")]
    [InlineData("-dx11  -krqlv=sd  -slno", "-dx11  -slno")]
    [InlineData("-dx11", "-dx11")]
    [InlineData(null, null)]
    public void RemoveTierArgument_KeepsEverythingElse(string? arguments, string? expected)
    {
        Assert.Equal(expected, KuroResourceTier.RemoveTierArgument(arguments));
    }



    /// <summary>
    /// 没有其他指示时：HD 优先（官方默认），没有 HD 用装了的最高一档，什么都没装是 HD
    /// </summary>
    [Theory]
    [InlineData(new string[0], null, "hd")]
    [InlineData(new[] { "uhd", "hd", "sd" }, null, "hd")]
    [InlineData(new[] { "uhd", "sd" }, null, "uhd")]
    [InlineData(new[] { "sd" }, null, "sd")]
    [InlineData(new[] { "hd", "sd" }, "sd", "sd")]
    [InlineData(new[] { "hd" }, "uhd", "hd")]
    [InlineData(new[] { "uhd", "hd" }, "UHD", "uhd")]
    public void ResolveLaunchTier_PrefersSelectionThenHd(string[] installed, string? preferred, string expected)
    {
        Assert.Equal(expected, KuroResourceTier.ResolveLaunchTier(installed, preferred));
    }



    [Fact]
    public void DecideLaunchTier_SelectionWinsAndDropsTheWrittenTier()
    {
        KuroLaunchTierDecision decision = KuroResourceTier.DecideLaunchTier(["uhd", "hd"], "uhd", "-krqlv=hd -dx11");

        Assert.Equal("uhd", decision.Tier);
        Assert.Equal("-krqlv=uhd", decision.TierArgument);
        Assert.Equal("-dx11", decision.StartArgument);
    }


    /// <summary>
    /// 照着网上的说明自己加了 -krqlv=uhd 的玩家，没在启动页选过时照他写的
    /// </summary>
    [Fact]
    public void DecideLaunchTier_KeepsAWrittenTierThatIsInstalled()
    {
        KuroLaunchTierDecision decision = KuroResourceTier.DecideLaunchTier(["uhd", "hd"], null, "-krqlv=uhd");

        Assert.Equal("uhd", decision.Tier);
        Assert.True(decision.FromStartArgument);
        Assert.Null(decision.TierArgument);
        Assert.Equal("-krqlv=uhd", decision.StartArgument);
    }


    /// <summary>
    /// 3.7.0 修好崩溃时的更新说明请玩家自己加过 -krqlv=hd；只装了流畅的人照它启动会找不到 HD
    /// </summary>
    [Fact]
    public void DecideLaunchTier_DropsAWrittenTierThatIsNotInstalled()
    {
        KuroLaunchTierDecision decision = KuroResourceTier.DecideLaunchTier(["sd"], null, "-krqlv=hd -slno");

        Assert.Equal("sd", decision.Tier);
        Assert.Equal("-krqlv=sd", decision.TierArgument);
        Assert.Equal("-slno", decision.StartArgument);
    }


    [Fact]
    public void DecideLaunchTier_DropsUnknownValues()
    {
        KuroLaunchTierDecision decision = KuroResourceTier.DecideLaunchTier(["hd"], null, "-krqlv=4k");

        Assert.Equal("hd", decision.Tier);
        Assert.Equal("-krqlv=hd", decision.TierArgument);
        Assert.Null(decision.StartArgument);
    }


    /// <summary>
    /// 读不到装了哪几档（例如只配置了第三方工具）时照指示走，都没有就是 HD
    /// </summary>
    [Fact]
    public void DecideLaunchTier_TrustsInstructionsWhenNothingIsDetected()
    {
        Assert.Equal("sd", KuroResourceTier.DecideLaunchTier([], "sd", null).Tier);
        Assert.Equal("uhd", KuroResourceTier.DecideLaunchTier([], null, "-krqlv=uhd").Tier);
        KuroLaunchTierDecision fallback = KuroResourceTier.DecideLaunchTier([], null, null);
        Assert.Equal("hd", fallback.Tier);
        Assert.Equal("-krqlv=hd", fallback.TierArgument);
        Assert.Null(fallback.StartArgument);
    }



    private async Task<GameLaunchCommand> LaunchAsync(FakeGameLaunchSettings settings)
    {
        GameDescriptor descriptor = KuroGameMapping.GetDescriptors()[0];
        string exe = Path.Combine(_root, descriptor.ExecutableName!);
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "");
        var catalog = new SimpleGameCatalogProvider(KuroGameMapping.ProviderId, KuroGameMapping.GetDescriptors);
        var provider = new KuroGameLaunchProvider(catalog, settings);
        return await provider.CreateLaunchCommandAsync(descriptor.Key, new GameLaunchOptions { InstallPath = _root }, TestContext.Current.CancellationToken);
    }


    /// <summary>
    /// 只装了流畅的玩家：以前写死的 -krqlv=hd 会让游戏找不到资源
    /// </summary>
    [Fact]
    public async Task KuroGameLaunchProvider_LaunchesTheInstalledTier()
    {
        AddTier(KuroResourceTier.SD);

        GameLaunchCommand command = await LaunchAsync(new FakeGameLaunchSettings());

        Assert.Equal("-krqlv=sd", command.Arguments);
    }


    [Fact]
    public async Task KuroGameLaunchProvider_PutsTheTierBeforeUserArgumentsExactlyOnce()
    {
        AddTier(KuroResourceTier.UHD);
        AddTier(KuroResourceTier.HD);

        GameLaunchCommand command = await LaunchAsync(new FakeGameLaunchSettings { ResourceTier = "uhd", StartArgument = "-krqlv=hd -custom", EnableDX11 = true });

        Assert.Equal("-krqlv=uhd -custom -dx11", command.Arguments);
    }


    [Fact]
    public async Task KuroGameLaunchProvider_IgnoresASelectionThatIsNoLongerInstalled()
    {
        AddTier(KuroResourceTier.HD);

        GameLaunchCommand command = await LaunchAsync(new FakeGameLaunchSettings { ResourceTier = "uhd" });

        Assert.Equal("-krqlv=hd", command.Arguments);
    }


    [Fact]
    public void KuroGameLaunchProvider_ReportsInstalledTiersAndTheOneItWillUse()
    {
        AddTier(KuroResourceTier.UHD);
        AddTier(KuroResourceTier.HD);
        var catalog = new SimpleGameCatalogProvider(KuroGameMapping.ProviderId, KuroGameMapping.GetDescriptors);

        IGameResourceTierProvider provider = new KuroGameLaunchProvider(catalog, new FakeGameLaunchSettings { StartArgument = "-krqlv=uhd" });
        GameResourceTierState state = provider.GetResourceTierState(KuroGameMapping.WutheringWavesGlobal, _root);

        Assert.Equal([KuroResourceTier.UHD, KuroResourceTier.HD], state.InstalledTiers);
        Assert.Equal(KuroResourceTier.UHD, state.LaunchTier);
    }

}
