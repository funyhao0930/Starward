using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Core.HoYoPlay;
using Starward.Features.GameLauncher;
using Starward.Features.HoYoPlay;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.HoYo;

/// <summary>
/// 米哈游游戏的启动页横幅与资讯，来自 HoYoPlay 的 getGameContent。
/// <para/>
/// 这一路本来直接写在 <see cref="GameBannerAndPost"/> 里，其他供应商接进来之后
/// 挪到这里，让每家自己管自己的接口。
/// </summary>
internal class HoYoLauncherContentProvider : IGameLauncherContentProvider
{

    private readonly HoYoPlayService _hoYoPlayService;


    public HoYoLauncherContentProvider(HoYoPlayService hoYoPlayService)
    {
        _hoYoPlayService = hoYoPlayService;
    }


    public string ProviderId => HoYoGameMapping.ProviderId;


    /// <summary>
    /// 只有 HoYoPlay 认识的游戏才有这支接口
    /// </summary>
    public bool Supports(GameKey key) => HoYoGameIds.Resolve(key) is not null;


    public async Task<GameContent?> GetContentAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (HoYoGameIds.Resolve(key) is not GameId gameId)
        {
            return null;
        }
        return await _hoYoPlayService.GetGameContentAsync(gameId);
    }

}
