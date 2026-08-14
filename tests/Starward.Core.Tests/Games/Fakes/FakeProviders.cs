using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Core.HoYoPlay;

namespace Starward.Core.Tests.Games.Fakes;

/// <summary>
/// 只提供一款游戏的目录 Provider
/// </summary>
internal class FakeCatalogProvider : IGameCatalogProvider
{
    private readonly GameDescriptor _descriptor;

    public FakeCatalogProvider(string providerId, GameKey key)
    {
        ProviderId = providerId;
        _descriptor = new GameDescriptor
        {
            Key = key,
            DisplayName = key.GameId,
            Capabilities = GameCapability.Launch | GameCapability.Discovery | GameCapability.Screenshot | GameCapability.PlayTime,
        };
    }

    public string ProviderId { get; }

    public int RefreshCount { get; private set; }

    public IReadOnlyList<GameDescriptor> GetGames() => [_descriptor];

    public GameDescriptor? GetGame(GameKey key) => key == _descriptor.Key ? _descriptor : null;

    public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        RefreshCount++;
        return ValueTask.CompletedTask;
    }
}


internal class FakeDiscoveryProvider : IGameDiscoveryProvider
{
    public FakeDiscoveryProvider(string providerId) => ProviderId = providerId;

    public string ProviderId { get; }

    public ValueTask<GameInstallation?> GetInstallationAsync(GameKey key, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<GameInstallation?>(null);

    public ValueTask<IReadOnlyList<GameInstallation>> DiscoverAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<GameInstallation>>([]);
}


internal class FakeLaunchProvider : IGameLaunchProvider
{
    private readonly string _exeName;

    public FakeLaunchProvider(string providerId, string exeName)
    {
        ProviderId = providerId;
        _exeName = exeName;
    }

    public string ProviderId { get; }

    public ValueTask<string?> GetExecutableNameAsync(GameKey key, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<string?>(_exeName);

    public ValueTask<GameLaunchCommand> CreateLaunchCommandAsync(GameKey key, GameLaunchOptions options, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new GameLaunchCommand { FileName = _exeName });
}


/// <summary>
/// 可控的启动设置
/// </summary>
internal class FakeGameLaunchSettings : IGameLaunchSettings
{
    public string? StartArgument { get; set; }

    public bool UsePopupWindow { get; set; }

    public bool EnableDX12 { get; set; }

    public bool EnableThirdPartyTool { get; set; }

    public string? ThirdPartyToolPath { get; set; }

    public bool StartGameWithCommandPrompt { get; set; }

    public int ClearThirdPartyToolPathCount { get; private set; }

    public string? GetStartArgument(GameKey key) => StartArgument;

    public bool GetUsePopupWindow(GameKey key) => UsePopupWindow;

    public bool GetEnableDX12(GameKey key) => EnableDX12;

    public bool GetEnableThirdPartyTool(GameKey key) => EnableThirdPartyTool;

    public string? GetThirdPartyToolPath(GameKey key) => ThirdPartyToolPath;

    public void ClearThirdPartyToolPath(GameKey key) => ClearThirdPartyToolPathCount++;
}


/// <summary>
/// 启动钩子。默认不产生票据，并且在被要求联网查询进程名时抛出，
/// 用来确保已适配的游戏不会走到网络回退路径。
/// </summary>
internal class FakeHoYoLaunchHooks : IHoYoLaunchHooks
{
    public string? AuthTicket { get; set; }

    public string? RemoteExecutableName { get; set; }

    public bool ThrowOnRemoteLookup { get; set; } = true;

    public int PreLaunchCount { get; private set; }

    public ValueTask<string?> CreateAuthTicketAsync(GameKey key, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(AuthTicket);

    public ValueTask ApplyPreLaunchSettingsAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        PreLaunchCount++;
        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> GetRemoteExecutableNameAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (ThrowOnRemoteLookup)
        {
            throw new InvalidOperationException("Tests must not hit the HoYoPlay network API.");
        }
        return ValueTask.FromResult(RemoteExecutableName);
    }
}


/// <summary>
/// 离线的游戏信息数据源
/// </summary>
internal class FakeHoYoGameInfoSource : IHoYoGameInfoSource
{
    public List<GameInfo> GameInfos { get; set; } = [];

    public int RefreshCount { get; private set; }

    public IReadOnlyList<GameInfo> GetCachedGameInfos() => GameInfos;

    public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        RefreshCount++;
        return ValueTask.CompletedTask;
    }
}
