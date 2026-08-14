using Starward.Core.Games;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Starward.Features.Gacha;

/// <summary>
/// 按供应商标识找到抽卡记录服务。
/// <para/>
/// 与 <see cref="IGameProviderRegistry"/> 同样的思路，但抽卡服务依赖应用层，
/// 无法登记进 Starward.Core 的注册表，因此单独一份。
/// </summary>
internal class GachaProviderRegistry
{

    private readonly Dictionary<string, IGameGachaProvider> _providers;


    public GachaProviderRegistry(IEnumerable<IGameGachaProvider> providers)
    {
        _providers = providers.ToDictionary(x => x.ProviderId, StringComparer.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 指定游戏的抽卡记录服务，该游戏没有抽卡功能时返回 null
    /// </summary>
    public GachaLogService? GetService(GameKey key)
    {
        if (!key.IsValid)
        {
            return null;
        }
        return _providers.TryGetValue(key.ProviderId, out IGameGachaProvider? provider) ? provider.GetService(key) : null;
    }

}
