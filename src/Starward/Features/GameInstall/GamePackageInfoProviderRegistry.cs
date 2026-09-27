using Starward.Core.Games;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Starward.Features.GameInstall;

/// <summary>
/// 按供应商标识找到安装包信息的 Provider，思路与 <see cref="GameLauncher.RoleCardProviderRegistry"/> 相同。
/// </summary>
public class GamePackageInfoProviderRegistry
{

    private readonly Dictionary<string, IGamePackageInfoProvider> _providers;


    public GamePackageInfoProviderRegistry(IEnumerable<IGamePackageInfoProvider> providers)
    {
        _providers = providers.ToDictionary(x => x.ProviderId, StringComparer.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 该游戏由 Starward 自己的下载器安装时返回对应的 Provider，否则返回 null
    /// </summary>
    public IGamePackageInfoProvider? GetProvider(GameKey key)
    {
        if (!key.IsValid)
        {
            return null;
        }
        if (_providers.TryGetValue(key.ProviderId, out IGamePackageInfoProvider? provider) && provider.Supports(key))
        {
            return provider;
        }
        return null;
    }

}
