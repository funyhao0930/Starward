using Starward.Core.Games;
using Starward.Core.Games.Kuro;
using Starward.Features.Gacha;

namespace Starward.Providers.Kuro;

/// <summary>
/// 库洛游戏的抽卡记录服务
/// </summary>
internal class KuroGachaProvider : IGameGachaProvider
{

    private readonly WuwaGachaService _wuwa;


    public KuroGachaProvider(WuwaGachaService wuwa)
    {
        _wuwa = wuwa;
    }


    public string ProviderId => KuroGameMapping.ProviderId;


    public GachaLogService? GetService(GameKey key)
    {
        if (!key.IsProvider(ProviderId))
        {
            return null;
        }
        return key.GameId switch
        {
            KuroGameMapping.WutheringWaves => _wuwa,
            _ => null,
        };
    }

}
