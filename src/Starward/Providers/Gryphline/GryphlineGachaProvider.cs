using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Features.Gacha;

namespace Starward.Providers.Gryphline;

/// <summary>
/// 鹰角网络的抽卡记录服务
/// </summary>
internal class GryphlineGachaProvider : IGameGachaProvider
{

    private readonly EndfieldGachaService _endfield;


    public GryphlineGachaProvider(EndfieldGachaService endfield)
    {
        _endfield = endfield;
    }


    public string ProviderId => GryphlineGameMapping.ProviderId;


    public GachaLogService? GetService(GameKey key)
    {
        if (!key.IsProvider(ProviderId))
        {
            return null;
        }
        return key.GameId switch
        {
            GryphlineGameMapping.Endfield => _endfield,
            _ => null,
        };
    }

}
