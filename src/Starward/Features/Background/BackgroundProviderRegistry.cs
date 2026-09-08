using Starward.Core.Games;
using Starward.Core.HoYoPlay;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Background;

/// <summary>
/// 按供应商标识找到在线背景图 Provider。
/// <para/>
/// 与 <c>GachaProviderRegistry</c> 同样的思路：在线背景图依赖应用层，
/// 无法登记进 Starward.Core 的 <see cref="IGameProviderRegistry"/>，因此单独一份。
/// </summary>
public class BackgroundProviderRegistry
{

    private readonly Dictionary<string, IGameBackgroundProvider> _providers;


    public BackgroundProviderRegistry(IEnumerable<IGameBackgroundProvider> providers)
    {
        _providers = providers.ToDictionary(x => x.ProviderId, StringComparer.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 该游戏有没有在线背景图接口
    /// </summary>
    public bool Supports(GameKey key)
    {
        return GetProvider(key) is not null;
    }


    /// <summary>
    /// 取得在线背景图，该游戏没有在线接口时返回空列表
    /// </summary>
    public async Task<List<GameBackground>> GetBackgroundsAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (GetProvider(key) is not IGameBackgroundProvider provider)
        {
            return [];
        }
        return await provider.GetBackgroundsAsync(key, cancellationToken);
    }


    private IGameBackgroundProvider? GetProvider(GameKey key)
    {
        if (!key.IsValid)
        {
            return null;
        }
        if (_providers.TryGetValue(key.ProviderId, out IGameBackgroundProvider? provider) && provider.Supports(key))
        {
            return provider;
        }
        return null;
    }

}
