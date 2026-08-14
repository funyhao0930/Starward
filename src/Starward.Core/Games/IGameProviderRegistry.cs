namespace Starward.Core.Games;

/// <summary>
/// 游戏供应商注册表，根据 ProviderId 找到对应的 Provider。
/// </summary>
public interface IGameProviderRegistry
{

    /// <summary>
    /// 所有已注册的目录 Provider
    /// </summary>
    IReadOnlyList<IGameCatalogProvider> CatalogProviders { get; }


    /// <summary>
    /// 所有已注册的搜索 Provider
    /// </summary>
    IReadOnlyList<IGameDiscoveryProvider> DiscoveryProviders { get; }


    /// <summary>
    /// 根据供应商标识获取目录 Provider，未注册时返回 null
    /// </summary>
    IGameCatalogProvider? GetCatalogProvider(string providerId);


    /// <summary>
    /// 根据供应商标识获取搜索 Provider，未注册时返回 null
    /// </summary>
    IGameDiscoveryProvider? GetDiscoveryProvider(string providerId);


    /// <summary>
    /// 根据供应商标识获取启动 Provider，未注册时返回 null
    /// </summary>
    IGameLaunchProvider? GetLaunchProvider(string providerId);


    /// <summary>
    /// 获取指定游戏的启动 Provider
    /// </summary>
    /// <exception cref="UnknownGameProviderException">供应商未注册</exception>
    IGameLaunchProvider GetRequiredLaunchProvider(GameKey key);


    /// <summary>
    /// 获取指定游戏的搜索 Provider
    /// </summary>
    /// <exception cref="UnknownGameProviderException">供应商未注册</exception>
    IGameDiscoveryProvider GetRequiredDiscoveryProvider(GameKey key);


    /// <summary>
    /// 合并所有目录 Provider 的游戏清单
    /// </summary>
    IReadOnlyList<GameDescriptor> GetAllGames();


    /// <summary>
    /// 获取指定游戏的描述，找不到时返回 null
    /// </summary>
    GameDescriptor? GetGame(GameKey key);


    /// <summary>
    /// 刷新所有目录 Provider
    /// </summary>
    ValueTask RefreshAllAsync(CancellationToken cancellationToken = default);

}
