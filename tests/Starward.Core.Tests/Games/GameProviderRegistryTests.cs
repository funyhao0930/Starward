using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Core.Tests.Games.Fakes;
using Xunit;

namespace Starward.Core.Tests.Games;

/// <summary>
/// Provider Registry 依 ProviderId 解析，以及未知供应商的错误处理
/// </summary>
public class GameProviderRegistryTests
{

    private static GameProviderRegistry CreateRegistry()
    {
        var hoyoCatalog = new HoYoCatalogProvider();
        var hoyoLaunch = new HoYoLaunchProvider(new FakeGameLaunchSettings(), new FakeHoYoLaunchHooks());
        var hoyoDiscovery = new FakeDiscoveryProvider(GameProviderIds.HoYo);
        var otherCatalog = new FakeCatalogProvider("kuro", new GameKey("kuro", "wuwa", "cn"));
        var otherLaunch = new FakeLaunchProvider("kuro", "Wuthering Waves.exe");
        return new GameProviderRegistry([hoyoCatalog, otherCatalog], [hoyoDiscovery], [hoyoLaunch, otherLaunch]);
    }


    [Fact]
    public void GetCatalogProvider_ReturnsProviderWithMatchingId()
    {
        GameProviderRegistry registry = CreateRegistry();
        Assert.IsType<HoYoCatalogProvider>(registry.GetCatalogProvider(GameProviderIds.HoYo));
        Assert.Equal("kuro", registry.GetCatalogProvider("kuro")?.ProviderId);
        Assert.Equal(2, registry.CatalogProviders.Count);
    }


    [Fact]
    public void GetCatalogProvider_IsCaseInsensitive()
    {
        GameProviderRegistry registry = CreateRegistry();
        Assert.NotNull(registry.GetCatalogProvider("HoYo"));
    }


    [Fact]
    public void GetLaunchProvider_ReturnsProviderWithMatchingId()
    {
        GameProviderRegistry registry = CreateRegistry();
        Assert.IsType<HoYoLaunchProvider>(registry.GetLaunchProvider(GameProviderIds.HoYo));
        Assert.IsType<FakeLaunchProvider>(registry.GetLaunchProvider("kuro"));
    }


    [Fact]
    public void GetDiscoveryProvider_ReturnsProviderWithMatchingId()
    {
        GameProviderRegistry registry = CreateRegistry();
        Assert.Equal(GameProviderIds.HoYo, registry.GetDiscoveryProvider(GameProviderIds.HoYo)?.ProviderId);
        Assert.Single(registry.DiscoveryProviders);
    }


    [Fact]
    public void GetProvider_ReturnsNullForUnknownProviderId()
    {
        GameProviderRegistry registry = CreateRegistry();
        Assert.Null(registry.GetCatalogProvider("nonexistent"));
        Assert.Null(registry.GetDiscoveryProvider("nonexistent"));
        Assert.Null(registry.GetLaunchProvider("nonexistent"));
    }


    [Fact]
    public void GetRequiredLaunchProvider_ThrowsForUnknownProvider()
    {
        GameProviderRegistry registry = CreateRegistry();
        var key = new GameKey("nonexistent", "game", "cn");
        var ex = Assert.Throws<UnknownGameProviderException>(() => registry.GetRequiredLaunchProvider(key));
        Assert.Equal("nonexistent", ex.ProviderId);
        Assert.Contains("nonexistent", ex.Message, StringComparison.Ordinal);
    }


    [Fact]
    public void GetRequiredDiscoveryProvider_ThrowsForProviderWithoutDiscovery()
    {
        GameProviderRegistry registry = CreateRegistry();
        // kuro 注册了目录与启动，但没有注册搜索
        var ex = Assert.Throws<UnknownGameProviderException>(() => registry.GetRequiredDiscoveryProvider(new GameKey("kuro", "wuwa", "cn")));
        Assert.Equal("kuro", ex.ProviderId);
    }


    [Fact]
    public void GetAllGames_MergesEveryCatalogProvider()
    {
        GameProviderRegistry registry = CreateRegistry();
        IReadOnlyList<GameDescriptor> games = registry.GetAllGames();
        Assert.Equal(HoYoGameMapping.SupportedGameKeys.Count + 1, games.Count);
        Assert.Contains(games, x => x.Key == new GameKey("kuro", "wuwa", "cn"));
        Assert.Contains(games, x => x.Key == HoYoGameMapping.FromGameBiz(GameBiz.hk4e_cn));
    }


    [Fact]
    public void GetGame_RoutesToOwningProvider()
    {
        GameProviderRegistry registry = CreateRegistry();
        Assert.Equal("hk4e", registry.GetGame(HoYoGameMapping.FromGameBiz(GameBiz.hk4e_global))?.Key.GameId);
        Assert.Equal("wuwa", registry.GetGame(new GameKey("kuro", "wuwa", "cn"))?.Key.GameId);
        Assert.Null(registry.GetGame(new GameKey("nonexistent", "game", "cn")));
    }


    [Fact]
    public async Task RefreshAllAsync_RefreshesEveryCatalogProvider()
    {
        var hoyo = new FakeCatalogProvider(GameProviderIds.HoYo, HoYoGameMapping.FromGameBiz(GameBiz.hk4e_cn));
        var kuro = new FakeCatalogProvider("kuro", new GameKey("kuro", "wuwa", "cn"));
        var registry = new GameProviderRegistry([hoyo, kuro], [], []);
        await registry.RefreshAllAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, hoyo.RefreshCount);
        Assert.Equal(1, kuro.RefreshCount);
    }


    [Fact]
    public void DuplicateProviderId_LastRegistrationWins()
    {
        var first = new FakeLaunchProvider("kuro", "first.exe");
        var second = new FakeLaunchProvider("kuro", "second.exe");
        var registry = new GameProviderRegistry([], [], [first, second]);
        Assert.Same(second, registry.GetLaunchProvider("kuro"));
    }

}
