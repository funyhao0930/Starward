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

    private readonly Dictionary<string, IGamePackageListProvider> _listProviders;


    public GamePackageInfoProviderRegistry(IEnumerable<IGamePackageInfoProvider> providers, IEnumerable<IGamePackageListProvider> listProviders)
    {
        _providers = providers.ToDictionary(x => x.ProviderId, StringComparer.OrdinalIgnoreCase);
        _listProviders = listProviders.ToDictionary(x => x.ProviderId, StringComparer.OrdinalIgnoreCase);
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


    /// <summary>
    /// 能列出线上资源包时返回对应的 Provider，否则返回 null。不代表 Starward 能安装这款游戏
    /// </summary>
    public IGamePackageListProvider? GetListProvider(GameKey key)
    {
        if (!key.IsValid)
        {
            return null;
        }
        if (_listProviders.TryGetValue(key.ProviderId, out IGamePackageListProvider? provider) && provider.Supports(key))
        {
            return provider;
        }
        return null;
    }

}
