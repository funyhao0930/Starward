namespace Starward.Core.Games;

/// <summary>
/// <see cref="IGameProviderRegistry"/> 的默认实现，通过构造函数注入所有已注册的 Provider。
/// </summary>
public class GameProviderRegistry : IGameProviderRegistry
{

    private readonly Dictionary<string, IGameCatalogProvider> _catalogProviders;

    private readonly Dictionary<string, IGameDiscoveryProvider> _discoveryProviders;

    private readonly Dictionary<string, IGameLaunchProvider> _launchProviders;


    public GameProviderRegistry(IEnumerable<IGameCatalogProvider> catalogProviders,
                                IEnumerable<IGameDiscoveryProvider> discoveryProviders,
                                IEnumerable<IGameLaunchProvider> launchProviders)
    {
        _catalogProviders = ToDictionary(catalogProviders);
        _discoveryProviders = ToDictionary(discoveryProviders);
        _launchProviders = ToDictionary(launchProviders);
        CatalogProviders = _catalogProviders.Values.ToList().AsReadOnly();
        DiscoveryProviders = _discoveryProviders.Values.ToList().AsReadOnly();
    }


    private static Dictionary<string, T> ToDictionary<T>(IEnumerable<T> providers) where T : IGameProvider
    {
        var dic = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (T provider in providers)
        {
            // 同一供应商重复注册时，后者覆盖前者
            dic[provider.ProviderId] = provider;
        }
        return dic;
    }


    public IReadOnlyList<IGameCatalogProvider> CatalogProviders { get; }


    public IReadOnlyList<IGameDiscoveryProvider> DiscoveryProviders { get; }


    public IGameCatalogProvider? GetCatalogProvider(string providerId)
    {
        return providerId is not null && _catalogProviders.TryGetValue(providerId, out IGameCatalogProvider? provider) ? provider : null;
    }


    public IGameDiscoveryProvider? GetDiscoveryProvider(string providerId)
    {
        return providerId is not null && _discoveryProviders.TryGetValue(providerId, out IGameDiscoveryProvider? provider) ? provider : null;
    }


    public IGameLaunchProvider? GetLaunchProvider(string providerId)
    {
        return providerId is not null && _launchProviders.TryGetValue(providerId, out IGameLaunchProvider? provider) ? provider : null;
    }


    public IGameLaunchProvider GetRequiredLaunchProvider(GameKey key)
    {
        return GetLaunchProvider(key.ProviderId)
            ?? throw new UnknownGameProviderException(key.ProviderId, $"No launch provider registered for game '{key}'.");
    }


    public IGameDiscoveryProvider GetRequiredDiscoveryProvider(GameKey key)
    {
        return GetDiscoveryProvider(key.ProviderId)
            ?? throw new UnknownGameProviderException(key.ProviderId, $"No discovery provider registered for game '{key}'.");
    }


    public IReadOnlyList<GameDescriptor> GetAllGames()
    {
        var games = new List<GameDescriptor>();
        foreach (IGameCatalogProvider provider in _catalogProviders.Values)
        {
            games.AddRange(provider.GetGames());
        }
        return games.AsReadOnly();
    }


    public GameDescriptor? GetGame(GameKey key)
    {
        return GetCatalogProvider(key.ProviderId)?.GetGame(key);
    }


    public async ValueTask RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (IGameCatalogProvider provider in _catalogProviders.Values)
        {
            await provider.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

}
