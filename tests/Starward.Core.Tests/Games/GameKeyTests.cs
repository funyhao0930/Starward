using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Xunit;

namespace Starward.Core.Tests.Games;

/// <summary>
/// GameBiz 与 GameKey 的相互转换
/// </summary>
public class GameKeyTests
{

    [Fact]
    public void AllGameBizs_RoundTripThroughGameKey()
    {
        Assert.NotEmpty(GameBiz.AllGameBizs);
        foreach (GameBiz gameBiz in GameBiz.AllGameBizs)
        {
            Assert.True(HoYoGameMapping.TryFromGameBiz(gameBiz, out GameKey key));
            Assert.Equal(GameProviderIds.HoYo, key.ProviderId);
            Assert.Equal(gameBiz.Game, key.GameId);
            Assert.Equal(gameBiz.Server, key.ChannelId);

            Assert.True(HoYoGameMapping.TryToGameBiz(key, out GameBiz roundTripped));
            Assert.Equal(gameBiz.Value, roundTripped.Value);
            // 转换与「是否提供」是两件事：被排除的游戏一样能正确转换
            Assert.Equal(!HoYoGameMapping.IsExcluded(key), HoYoGameMapping.IsSupported(key));
        }
    }


    [Theory]
    [InlineData("hk4e_cn", "hk4e", "cn")]
    [InlineData("hk4e_global", "hk4e", "global")]
    [InlineData("hk4e_bilibili", "hk4e", "bilibili")]
    [InlineData("bh3_cn", "bh3", "cn")]
    [InlineData("hkrpg_global", "hkrpg", "global")]
    [InlineData("nap_bilibili", "nap", "bilibili")]
    public void FromGameBiz_SplitsGameAndChannel(string biz, string expectedGame, string expectedChannel)
    {
        GameKey key = HoYoGameMapping.FromGameBiz(biz);
        Assert.Equal(new GameKey(GameProviderIds.HoYo, expectedGame, expectedChannel), key);
    }


    [Theory]
    [InlineData("")]
    [InlineData("hk4e")]
    [InlineData(null)]
    public void TryFromGameBiz_ReturnsFalseWithoutThrowing(string? biz)
    {
        Assert.False(HoYoGameMapping.TryFromGameBiz(new GameBiz(biz), out GameKey key));
        Assert.Equal(default, key);
    }


    [Fact]
    public void FromGameBiz_ThrowsForUnsplittableValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HoYoGameMapping.FromGameBiz("hk4e"));
    }


    /// <summary>
    /// HoYoPlay 返回但尚未适配的游戏也能转换，只是不算已支持
    /// </summary>
    [Fact]
    public void UnadaptedGameBiz_ConvertsButIsNotSupported()
    {
        Assert.True(HoYoGameMapping.TryFromGameBiz("hk4e_os", out GameKey key));
        Assert.Equal(new GameKey(GameProviderIds.HoYo, "hk4e", "os"), key);
        Assert.False(HoYoGameMapping.IsSupported(key));
    }


    [Fact]
    public void TryToGameBiz_ReturnsFalseForOtherProviders()
    {
        var key = new GameKey("kuro", "wuwa", "cn");
        Assert.False(HoYoGameMapping.TryToGameBiz(key, out GameBiz gameBiz));
        Assert.Equal(default, gameBiz);
        Assert.Throws<ArgumentOutOfRangeException>(() => HoYoGameMapping.ToGameBiz(key));
    }


    [Fact]
    public void ToString_UsesCanonicalForm()
    {
        Assert.Equal("hoyo:hk4e:cn", new GameKey("hoyo", "hk4e", "cn").ToString());
    }


    [Theory]
    [InlineData("hoyo:hk4e:cn", true)]
    [InlineData("kuro:wuwa:global", true)]
    [InlineData("hoyo:hk4e", false)]
    [InlineData("hoyo:hk4e:cn:extra", false)]
    [InlineData("hoyo::cn", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_HandlesCanonicalForm(string? value, bool expected)
    {
        Assert.Equal(expected, GameKey.TryParse(value, out GameKey key));
        if (expected)
        {
            Assert.Equal(value, key.ToString());
        }
    }


    [Fact]
    public void IsSameGame_IgnoresChannel()
    {
        GameKey cn = HoYoGameMapping.FromGameBiz(GameBiz.hk4e_cn);
        GameKey global = HoYoGameMapping.FromGameBiz(GameBiz.hk4e_global);
        GameKey starRail = HoYoGameMapping.FromGameBiz(GameBiz.hkrpg_cn);
        Assert.True(cn.IsSameGame(global));
        Assert.False(cn.IsSameGame(starRail));
        Assert.NotEqual(cn, global);
    }


    /// <summary>
    /// 支持的游戏是上游数据去掉本分支排除的游戏
    /// </summary>
    [Fact]
    public void SupportedGameKeys_AreAllGameBizsMinusExcluded()
    {
        List<string> expected = GameBiz.AllGameBizs
            .Select(HoYoGameMapping.FromGameBiz)
            .Where(x => !HoYoGameMapping.IsExcluded(x))
            .Select(x => HoYoGameMapping.ToGameBiz(x).Value)
            .ToList();
        Assert.Equal(expected, HoYoGameMapping.SupportedGameKeys.Select(x => HoYoGameMapping.ToGameBiz(x).Value).ToList());
        Assert.DoesNotContain(HoYoGameMapping.SupportedGameKeys, HoYoGameMapping.IsExcluded);
    }


    /// <summary>
    /// 本分支不提供崩坏3
    /// </summary>
    [Fact]
    public void HonkaiImpact3rd_IsExcluded()
    {
        Assert.True(HoYoGameMapping.IsExcluded(HoYoGameMapping.FromGameBiz(GameBiz.bh3_cn)));
        Assert.True(HoYoGameMapping.IsExcluded(HoYoGameMapping.FromGameBiz(GameBiz.bh3_global)));
        Assert.False(HoYoGameMapping.IsExcluded(HoYoGameMapping.FromGameBiz(GameBiz.hk4e_cn)));
        Assert.Empty(HoYoGameMapping.SupportedGameKeys.Where(x => x.GameId is GameBiz.bh3));
    }

}
