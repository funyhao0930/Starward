using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Features.Gacha;

namespace Starward.Providers.HoYo;

/// <summary>
/// 米哈游游戏的抽卡记录服务。
/// 崩坏3没有抽卡记录功能，因此不在此列。
/// </summary>
internal class HoYoGachaProvider : IGameGachaProvider
{

    private readonly GenshinGachaService _genshin;

    private readonly StarRailGachaService _starRail;

    private readonly ZZZGachaService _zzz;


    public HoYoGachaProvider(GenshinGachaService genshin, StarRailGachaService starRail, ZZZGachaService zzz)
    {
        _genshin = genshin;
        _starRail = starRail;
        _zzz = zzz;
    }


    public string ProviderId => HoYoGameMapping.ProviderId;


    public GachaLogService? GetService(GameKey key)
    {
        if (!key.IsProvider(ProviderId))
        {
            return null;
        }
        return key.GameId switch
        {
            HoYoGameMapping.Hk4e => _genshin,
            HoYoGameMapping.Hkrpg => _starRail,
            HoYoGameMapping.Nap => _zzz,
            _ => null,
        };
    }

}
