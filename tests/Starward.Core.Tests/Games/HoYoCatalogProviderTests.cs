using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Core.HoYoPlay;
using Starward.Core.Tests.Games.Fakes;
using Xunit;

namespace Starward.Core.Tests.Games;

/// <summary>
/// HoYoCatalogProvider 的游戏清单。全部离线，游戏信息由假的数据源提供。
/// </summary>
public class HoYoCatalogProviderTests
{

    private static GameInfo CreateGameInfo(string id, string biz, string name)
    {
        return new GameInfo
        {
            Id = id,
            GameBiz = biz,
            Display = new GameInfoDisplay
            {
                Name = name,
                Icon = new GameImage { Url = $"https://example.invalid/{id}/icon.png" },
                Logo = new GameImage { Url = $"https://example.invalid/{id}/logo.png" },
                Thumbnail = new GameImage { Url = $"https://example.invalid/{id}/thumb.png" },
            },
        };
    }


    [Fact]
    public void GetGames_ReturnsEveryAdaptedGameWithoutGameInfo()
    {
        var catalog = new HoYoCatalogProvider();
        IReadOnlyList<GameDescriptor> games = catalog.GetGames();

        Assert.Equal(GameBiz.AllGameBizs.Count, games.Count);
        Assert.All(games, x => Assert.Equal(GameProviderIds.HoYo, x.Key.ProviderId));
        Assert.Equal(GameBiz.AllGameBizs.Select(x => x.Value).ToList(), games.Select(x => x.LegacyGameBiz!).ToList());
    }


    [Fact]
    public void GetGames_FillsArtworkFromGameInfo()
    {
        var source = new FakeHoYoGameInfoSource
        {
            GameInfos = [CreateGameInfo("1Z8W5NHUQb", GameBiz.hk4e_cn, "原神")],
        };
        var catalog = new HoYoCatalogProvider(source);

        GameDescriptor genshin = catalog.GetGames().First(x => x.LegacyGameBiz == GameBiz.hk4e_cn);
        Assert.Equal("https://example.invalid/1Z8W5NHUQb/thumb.png", genshin.ThumbnailUri);
        Assert.Equal("https://example.invalid/1Z8W5NHUQb/logo.png", genshin.LogoUri);
        // 已适配的游戏保留内置的图标与名称
        Assert.Equal("ms-appx:///Assets/Image/icon_ys.jpg", genshin.IconUri);
        Assert.Equal("1Z8W5NHUQb", genshin.ProviderGameId);
    }


    /// <summary>
    /// HoYoPlay 返回但尚未适配的游戏也要出现在清单中，并使用接口返回的名称与图标
    /// </summary>
    [Fact]
    public void GetGames_IncludesUnadaptedGames()
    {
        var source = new FakeHoYoGameInfoSource
        {
            GameInfos = [CreateGameInfo("NewGameId", "newgame_cn", "新游戏")],
        };
        var catalog = new HoYoCatalogProvider(source);
        IReadOnlyList<GameDescriptor> games = catalog.GetGames();

        Assert.Equal(GameBiz.AllGameBizs.Count + 1, games.Count);
        GameDescriptor newGame = games.First(x => x.Key.GameId == "newgame");
        Assert.Equal("新游戏", newGame.DisplayName);
        Assert.Equal("https://example.invalid/NewGameId/icon.png", newGame.IconUri);
        Assert.Equal("NewGameId", newGame.ProviderGameId);
        Assert.Equal(GameCapability.Launch, newGame.Capabilities);
    }


    /// <summary>
    /// Bilibili 渠道的游戏信息 biz 字段与国服相同，需要按 Id 修正渠道
    /// </summary>
    [Fact]
    public void GetGames_NormalizesBilibiliChannel()
    {
        var source = new FakeHoYoGameInfoSource
        {
            // T2S0Gz4Dr2 是原神 Bilibili 渠道服的 Id，但接口返回的 biz 是 hk4e_cn
            GameInfos = [CreateGameInfo("T2S0Gz4Dr2", GameBiz.hk4e_cn, "原神")],
        };
        var catalog = new HoYoCatalogProvider(source);

        GameDescriptor bilibili = catalog.GetGames().First(x => x.LegacyGameBiz == GameBiz.hk4e_bilibili);
        Assert.Equal("https://example.invalid/T2S0Gz4Dr2/thumb.png", bilibili.ThumbnailUri);
        // 已适配的游戏使用内置的 GameId，而不是接口返回的
        Assert.Equal("T2S0Gz4Dr2", bilibili.ProviderGameId);
        Assert.Equal(GameChannelIds.Bilibili, bilibili.Key.ChannelId);

        // 国服没有对应的游戏信息，因此没有图片
        GameDescriptor cn = catalog.GetGames().First(x => x.LegacyGameBiz == GameBiz.hk4e_cn);
        Assert.Null(cn.ThumbnailUri);
    }


    /// <summary>
    /// 游戏选择器按 (ProviderId, GameId) 分组显示游戏，每组内是该游戏的所有渠道。
    /// 现有 4 款米哈游游戏必须分成 4 组，渠道与重构前一致。
    /// </summary>
    [Fact]
    public void GetGames_GroupsIntoFourHoYoGamesWithExpectedChannels()
    {
        var catalog = new HoYoCatalogProvider();
        Dictionary<string, string[]> channels = catalog.GetGames()
            .GroupBy(x => x.Key.GameId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Key.ChannelId).ToArray());

        Assert.Equal(4, channels.Count);
        Assert.Equal(["cn", "global"], channels["bh3"]);
        Assert.Equal(["cn", "global", "bilibili"], channels["hk4e"]);
        Assert.Equal(["cn", "global", "bilibili"], channels["hkrpg"]);
        Assert.Equal(["cn", "global", "bilibili"], channels["nap"]);
    }


    [Fact]
    public void GetGame_ReturnsNullForOtherProviders()
    {
        var catalog = new HoYoCatalogProvider();
        Assert.Null(catalog.GetGame(new GameKey("kuro", "wuwa", "cn")));
        Assert.Null(catalog.GetGame(default));
    }


    [Fact]
    public void GetGame_ReturnsNullForUnknownHoYoGameWithoutGameInfo()
    {
        var catalog = new HoYoCatalogProvider();
        Assert.Null(catalog.GetGame(new GameKey(GameProviderIds.HoYo, "newgame", "cn")));
    }


    [Fact]
    public async Task RefreshAsync_DelegatesToGameInfoSource()
    {
        var source = new FakeHoYoGameInfoSource();
        var catalog = new HoYoCatalogProvider(source);
        await catalog.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, source.RefreshCount);
    }


    [Fact]
    public async Task RefreshAsync_WithoutGameInfoSource_DoesNotThrow()
    {
        await new HoYoCatalogProvider().RefreshAsync(TestContext.Current.CancellationToken);
    }

}
