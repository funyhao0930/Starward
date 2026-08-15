using Starward.Core.Games;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Starward.Features.GameSetting;

/// <summary>
/// 按供应商标识找到画面设置的读写实现。
/// 与 <see cref="Gacha.GachaProviderRegistry"/> 同样的思路。
/// </summary>
internal class GameSettingProviderRegistry
{

    private readonly Dictionary<string, IGameSettingProvider> _providers;


    public GameSettingProviderRegistry(IEnumerable<IGameSettingProvider> providers)
    {
        _providers = providers.ToDictionary(x => x.ProviderId, StringComparer.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 指定游戏的画面设置实现，不支持时返回 null
    /// </summary>
    public IGameSettingProvider? GetProvider(GameKey key)
    {
        if (!key.IsValid)
        {
            return null;
        }
        return _providers.TryGetValue(key.ProviderId, out IGameSettingProvider? provider) ? provider : null;
    }

}
