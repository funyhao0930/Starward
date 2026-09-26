using Starward.Core.Games;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 按供应商标识找到启动页角色卡片的 Provider，思路与 <see cref="LauncherContentProviderRegistry"/> 相同。
/// </summary>
public class RoleCardProviderRegistry
{

    private readonly Dictionary<string, IGameRoleCardProvider> _providers;


    public RoleCardProviderRegistry(IEnumerable<IGameRoleCardProvider> providers)
    {
        _providers = providers.ToDictionary(x => x.ProviderId, StringComparer.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 该游戏有角色卡片时返回对应的 Provider，否则返回 null
    /// </summary>
    public IGameRoleCardProvider? GetProvider(GameKey key)
    {
        if (!key.IsValid)
        {
            return null;
        }
        if (_providers.TryGetValue(key.ProviderId, out IGameRoleCardProvider? provider) && provider.Supports(key))
        {
            return provider;
        }
        return null;
    }

}
