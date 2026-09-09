using Starward.Core.Games;
using Starward.Core.Games.Hotta;
using Starward.Features.Gacha;

namespace Starward.Providers.Hotta;

/// <summary>
/// 完美世界（异环）的抽卡记录服务
/// </summary>
internal class HottaGachaProvider : IGameGachaProvider
{

    private readonly NteGachaService _nte;


    public HottaGachaProvider(NteGachaService nte)
    {
        _nte = nte;
    }


    public string ProviderId => HottaGameMapping.ProviderId;


    public GachaLogService? GetService(GameKey key)
    {
        if (!key.IsProvider(ProviderId))
        {
            return null;
        }
        return key.GameId switch
        {
            HottaGameMapping.NevernessToEverness => _nte,
            _ => null,
        };
    }

}
