using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Core.HoYoPlay;
using Starward.Features.Background;
using Starward.Features.HoYoPlay;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.HoYo;

/// <summary>
/// 米哈游游戏的在线背景图，来自 HoYoPlay 的 <c>getAllGameBasicInfo</c>。
/// <para/>
/// 这一路本来写在 <see cref="BackgroundService"/> 里，其他供应商接进来之后
/// 挪到这里，让每家自己管自己的接口。
/// </summary>
internal class HoYoBackgroundProvider : IGameBackgroundProvider
{

    private readonly HoYoPlayService _hoYoPlayService;


    public HoYoBackgroundProvider(HoYoPlayService hoYoPlayService)
    {
        _hoYoPlayService = hoYoPlayService;
    }


    public string ProviderId => HoYoGameMapping.ProviderId;


    /// <summary>
    /// 只有 HoYoPlay 认识的游戏才有在线背景图。
    /// 解析不出 <see cref="GameId"/> 的（例如只支持启动的老渠道）没有。
    /// </summary>
    public bool Supports(GameKey key) => HoYoGameIds.Resolve(key) is not null;


    public async Task<List<GameBackground>> GetBackgroundsAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (HoYoGameIds.Resolve(key) is not GameId gameId)
        {
            return [];
        }
        GameBackgroundInfo backgroundInfo = await _hoYoPlayService.GetGameBackgroundAsync(gameId, cancellationToken);
        List<GameBackground> backgrounds = backgroundInfo?.Backgrounds?.ToList() ?? [];
        GameInfo gameInfo = await _hoYoPlayService.GetGameInfoAsync(gameId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(gameInfo?.Display?.Background?.Url))
        {
            backgrounds.Add(GameBackground.FromPosterUrl(gameInfo.Display.Background.Url));
        }
        return backgrounds;
    }

}
