using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Core.Tests.Games.Fakes;
using Xunit;

namespace Starward.Core.Tests.Games;

/// <summary>
/// HoYoLaunchProvider 的进程名与启动命令构建。全部离线，不接触真实的 HoYoPlay 接口。
/// </summary>
public class HoYoLaunchProviderTests : IDisposable
{

    private readonly FakeGameLaunchSettings _settings = new();

    private readonly FakeHoYoLaunchHooks _hooks = new();

    private readonly HoYoLaunchProvider _provider;

    private readonly string _installPath;


    public HoYoLaunchProviderTests()
    {
        _provider = new HoYoLaunchProvider(_settings, _hooks);
        _installPath = Path.Combine(Path.GetTempPath(), "StarwardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_installPath);
    }


    public void Dispose()
    {
        try
        {
            Directory.Delete(_installPath, true);
        }
        catch { }
    }


    private string CreateGameExe(string exeName)
    {
        string path = Path.Combine(_installPath, exeName);
        File.WriteAllText(path, "");
        return path;
    }


    [Theory]
    [InlineData(GameBiz.bh3_cn, "BH3.exe")]
    [InlineData(GameBiz.bh3_global, "BH3.exe")]
    [InlineData(GameBiz.hk4e_cn, "YuanShen.exe")]
    [InlineData(GameBiz.hk4e_bilibili, "YuanShen.exe")]
    [InlineData(GameBiz.hk4e_global, "GenshinImpact.exe")]
    [InlineData(GameBiz.hkrpg_cn, "StarRail.exe")]
    [InlineData(GameBiz.hkrpg_global, "StarRail.exe")]
    [InlineData(GameBiz.hkrpg_bilibili, "StarRail.exe")]
    [InlineData(GameBiz.nap_cn, "ZenlessZoneZero.exe")]
    [InlineData(GameBiz.nap_global, "ZenlessZoneZero.exe")]
    [InlineData(GameBiz.nap_bilibili, "ZenlessZoneZero.exe")]
    public async Task GetExecutableNameAsync_ReturnsCorrectExeWithoutNetwork(string biz, string expected)
    {
        // _hooks.ThrowOnRemoteLookup 为 true，走到联网回退就会失败
        string? name = await _provider.GetExecutableNameAsync(HoYoGameMapping.FromGameBiz(biz), TestContext.Current.CancellationToken);
        Assert.Equal(expected, name);
    }


    [Fact]
    public async Task GetExecutableNameAsync_FallsBackToProviderLookupForUnknownGame()
    {
        _hooks.ThrowOnRemoteLookup = false;
        _hooks.RemoteExecutableName = "CloudGenshin.exe";
        string? name = await _provider.GetExecutableNameAsync(HoYoGameMapping.FromGameBiz("clgm_cn"), TestContext.Current.CancellationToken);
        Assert.Equal("CloudGenshin.exe", name);
    }


    [Fact]
    public async Task CreateLaunchCommandAsync_UsesRequestedInstallPath()
    {
        string exe = CreateGameExe("YuanShen.exe");
        GameLaunchCommand command = await _provider.CreateLaunchCommandAsync(
            HoYoGameMapping.FromGameBiz(GameBiz.hk4e_cn),
            new GameLaunchOptions { InstallPath = _installPath },
            TestContext.Current.CancellationToken);

        Assert.Equal(exe, command.FileName);
        Assert.Equal(_installPath, command.WorkingDirectory);
        Assert.Null(command.Verb);
        Assert.True(command.UseShellExecute);
        Assert.False(command.TrackByProcessName);
        Assert.Equal(1, _hooks.PreLaunchCount);
    }


    /// <summary>
    /// 找不到指定目录时退回配置中记录的目录，并请求管理员权限
    /// </summary>
    [Fact]
    public async Task CreateLaunchCommandAsync_FallsBackToConfiguredInstallPath()
    {
        string exe = CreateGameExe("StarRail.exe");
        GameLaunchCommand command = await _provider.CreateLaunchCommandAsync(
            HoYoGameMapping.FromGameBiz(GameBiz.hkrpg_cn),
            new GameLaunchOptions { ConfiguredInstallPath = _installPath },
            TestContext.Current.CancellationToken);

        Assert.Equal(exe, command.FileName);
        Assert.Equal("runas", command.Verb);
    }


    [Fact]
    public async Task CreateLaunchCommandAsync_ThrowsWhenExeMissing()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await _provider.CreateLaunchCommandAsync(
                HoYoGameMapping.FromGameBiz(GameBiz.nap_cn),
                new GameLaunchOptions { ConfiguredInstallPath = _installPath },
                TestContext.Current.CancellationToken));
    }


    [Fact]
    public async Task CreateLaunchCommandAsync_ComposesArguments()
    {
        CreateGameExe("YuanShen.exe");
        _settings.StartArgument = "  -custom  ";
        _settings.UsePopupWindow = true;
        _settings.EnableDX12 = true;
        _hooks.AuthTicket = "abc123";

        GameLaunchCommand command = await _provider.CreateLaunchCommandAsync(
            HoYoGameMapping.FromGameBiz(GameBiz.hk4e_cn),
            new GameLaunchOptions { InstallPath = _installPath },
            TestContext.Current.CancellationToken);

        Assert.Equal("-custom login_auth_ticket=abc123 -popupwindow -use-d3d12", command.Arguments);
    }


    [Fact]
    public async Task CreateLaunchCommandAsync_OmitsOptionalArguments()
    {
        CreateGameExe("BH3.exe");
        GameLaunchCommand command = await _provider.CreateLaunchCommandAsync(
            HoYoGameMapping.FromGameBiz(GameBiz.bh3_cn),
            new GameLaunchOptions { InstallPath = _installPath },
            TestContext.Current.CancellationToken);
        Assert.Null(command.Arguments);
    }


    [Fact]
    public async Task CreateLaunchCommandAsync_WrapsWithCommandPrompt()
    {
        string exe = CreateGameExe("ZenlessZoneZero.exe");
        _settings.StartGameWithCommandPrompt = true;

        GameLaunchCommand command = await _provider.CreateLaunchCommandAsync(
            HoYoGameMapping.FromGameBiz(GameBiz.nap_cn),
            new GameLaunchOptions { InstallPath = _installPath },
            TestContext.Current.CancellationToken);

        Assert.Equal("cmd.exe", command.FileName);
        Assert.Equal($"""/c start "" /d "{_installPath}" "{exe}" """, command.Arguments);
        Assert.True(command.TrackByProcessName);
    }


    [Fact]
    public async Task CreateLaunchCommandAsync_UsesThirdPartyTool()
    {
        // 不创建游戏本体，强制走第三方工具
        string tool = Path.Combine(_installPath, "tool.exe");
        File.WriteAllText(tool, "");
        _settings.EnableThirdPartyTool = true;
        _settings.ThirdPartyToolPath = tool;
        _settings.StartGameWithCommandPrompt = true;

        GameLaunchCommand command = await _provider.CreateLaunchCommandAsync(
            HoYoGameMapping.FromGameBiz(GameBiz.hk4e_global),
            new GameLaunchOptions { InstallPath = _installPath },
            TestContext.Current.CancellationToken);

        Assert.Equal(tool, command.FileName);
        Assert.Equal("runas", command.Verb);
        Assert.True(command.TrackByProcessName);
        // 第三方工具不经过 cmd.exe 包装
        Assert.DoesNotContain("cmd.exe", command.FileName, StringComparison.Ordinal);
    }


    [Fact]
    public async Task CreateLaunchCommandAsync_ClearsMissingThirdPartyTool()
    {
        CreateGameExe("GenshinImpact.exe");
        _settings.EnableThirdPartyTool = true;
        _settings.ThirdPartyToolPath = Path.Combine(_installPath, "does-not-exist.exe");

        GameLaunchCommand command = await _provider.CreateLaunchCommandAsync(
            HoYoGameMapping.FromGameBiz(GameBiz.hk4e_global),
            new GameLaunchOptions { ConfiguredInstallPath = _installPath },
            TestContext.Current.CancellationToken);

        Assert.Equal(1, _settings.ClearThirdPartyToolPathCount);
        Assert.EndsWith("GenshinImpact.exe", command.FileName, StringComparison.Ordinal);
    }


    [Fact]
    public async Task CreateLaunchCommandAsync_ThrowsForUnknownGame()
    {
        _hooks.ThrowOnRemoteLookup = false;
        _hooks.RemoteExecutableName = null;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await _provider.CreateLaunchCommandAsync(new GameKey("hoyo", "unknown", "cn"), new GameLaunchOptions(), TestContext.Current.CancellationToken));
    }


    [Fact]
    public void ProviderId_IsHoYo()
    {
        Assert.Equal(GameProviderIds.HoYo, _provider.ProviderId);
    }

}
