using Starward.Core;
using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Core.HoYoPlay;
using Starward.Features.GameLauncher;
using Starward.Features.GameSetting;
using Starward.Features.HoYoPlay;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.HoYo;

/// <summary>
/// 米哈游游戏启动过程中需要应用层协助的操作。
/// </summary>
internal class HoYoLaunchHooks : IHoYoLaunchHooks
{

    private readonly GameAuthLoginService _gameAuthLoginService;

    private readonly HoYoPlayService _hoYoPlayService;


    public HoYoLaunchHooks(GameAuthLoginService gameAuthLoginService, HoYoPlayService hoYoPlayService)
    {
        _gameAuthLoginService = gameAuthLoginService;
        _hoYoPlayService = hoYoPlayService;
    }


    public async ValueTask<string?> CreateAuthTicketAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (AppConfig.EnableLoginAuthTicket is not true)
        {
            return null;
        }
        if (TryGetGameId(key) is not GameId gameId)
        {
            return null;
        }
        return await _gameAuthLoginService.CreateAuthTicketByGameBiz(gameId);
    }


    public ValueTask ApplyPreLaunchSettingsAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        // 原神的 HDR 设置写在注册表中，启动前需要同步一次
        if (key.IsProvider(HoYoGameMapping.ProviderId)
            && key.GameId is HoYoGameMapping.Hk4e
            && HoYoGameMapping.TryToGameBiz(key, out GameBiz gameBiz))
        {
            GameSettingService.SetGenshinEnableHDR(gameBiz, AppConfig.EnableGenshinHDR);
        }
        return ValueTask.CompletedTask;
    }


    public async ValueTask<string?> GetRemoteExecutableNameAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (TryGetGameId(key) is not GameId gameId)
        {
            return null;
        }
        GameConfig? config = await _hoYoPlayService.GetGameConfigAsync(gameId, cancellationToken);
        return config?.ExeFileName;
    }


    private static GameId? TryGetGameId(GameKey key) => HoYoGameIds.Resolve(key);

}
