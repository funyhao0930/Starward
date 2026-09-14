using Starward.Core.Games;
using Starward.Core.HoYoPlay;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 按供应商标识找到启动页横幅与资讯的 Provider。
/// <para/>
/// 与 <c>BackgroundProviderRegistry</c> 同样的思路：这一路依赖应用层，
/// 无法登记进 Starward.Core 的 <see cref="IGameProviderRegistry"/>，因此单独一份。
/// </summary>
public class LauncherContentProviderRegistry
{

    private readonly Dictionary<string, IGameLauncherContentProvider> _providers;


    public LauncherContentProviderRegistry(IEnumerable<IGameLauncherContentProvider> providers)
    {
        _providers = providers.ToDictionary(x => x.ProviderId, StringComparer.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 该游戏有没有横幅与资讯接口
    /// </summary>
    public bool Supports(GameKey key)
    {
        return GetProvider(key) is not null;
    }


    /// <summary>
    /// 取得横幅与资讯，该游戏没有接口时返回 null
    /// </summary>
    public async Task<GameContent?> GetContentAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (GetProvider(key) is not IGameLauncherContentProvider provider)
        {
            return null;
        }
        return await provider.GetContentAsync(key, cancellationToken);
    }


    private IGameLauncherContentProvider? GetProvider(GameKey key)
    {
        if (!key.IsValid)
        {
            return null;
        }
        if (_providers.TryGetValue(key.ProviderId, out IGameLauncherContentProvider? provider) && provider.Supports(key))
        {
            return provider;
        }
        return null;
    }

}
