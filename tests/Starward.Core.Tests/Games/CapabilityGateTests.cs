using Starward.Core.Games;
using Starward.Core.HoYoPlay;
using Starward.Core.Games.Gryphline;
using Starward.Core.Games.HoYo;
using Starward.Core.Games.Hotta;
using Starward.Core.Games.Kuro;
using Starward.Core.Tests.Games.Fakes;
using Xunit;

namespace Starward.Core.Tests.Games;

/// <summary>
/// 能力闸门：调用只有部分供应商才有的接口之前，必须能判断出该游戏支持不支持。
/// 这类判断以前散落在各调用端，靠实际跑出异常才发现漏网，现在集中在一处。
/// </summary>
public class CapabilityGateTests
{

    private static GameProviderRegistry CreateRegistry()
    {
        return new GameProviderRegistry(
        [
            new HoYoCatalogProvider(),
            new SimpleGameCatalogProvider(KuroGameMapping.ProviderId, KuroGameMapping.GetDescriptors),
            new SimpleGameCatalogProvider(HottaGameMapping.ProviderId, HottaGameMapping.GetDescriptors),
            new SimpleGameCatalogProvider(GryphlineGameMapping.ProviderId, GryphlineGameMapping.GetDescriptors),
        ], [], []);
    }


    /// <summary>
    /// 米哈游游戏有在线安装包接口，三款只支持启动的游戏没有。
    /// 这正是背景图、音频语言、游戏资源等区块的判断依据。
    /// </summary>
    [Fact]
    public void SupportsCapability_SeparatesHoYoFromLaunchOnlyGames()
    {
        GameProviderRegistry registry = CreateRegistry();

        foreach (GameKey key in HoYoGameMapping.SupportedGameKeys)
        {
            Assert.True(registry.SupportsCapability(key, GameCapability.Install));
        }

        Assert.False(registry.SupportsCapability(KuroGameMapping.WutheringWavesGlobal, GameCapability.Install));
        Assert.False(registry.SupportsCapability(HottaGameMapping.NevernessToEvernessTaiwan, GameCapability.Install));
        Assert.False(registry.SupportsCapability(GryphlineGameMapping.EndfieldDefault, GameCapability.Install));
    }


    /// <summary>
    /// 三款新游戏都支持启动与截图，所以工具栏上会有首页与截图两项
    /// </summary>
    [Fact]
    public void SupportsCapability_LaunchOnlyGamesStillHaveLaunchAndScreenshot()
    {
        GameProviderRegistry registry = CreateRegistry();
        foreach (GameKey key in new[] { KuroGameMapping.WutheringWavesGlobal,
                                        HottaGameMapping.NevernessToEvernessTaiwan,
                                        GryphlineGameMapping.EndfieldDefault })
        {
            Assert.True(registry.SupportsCapability(key, GameCapability.Launch));
            Assert.True(registry.SupportsCapability(key, GameCapability.Screenshot));
            Assert.True(registry.SupportsCapability(key, GameCapability.PlayTime));
        }
    }


    /// <summary>
    /// 无效的键不支持任何能力
    /// </summary>
    [Fact]
    public void SupportsCapability_InvalidKeyIsFalse()
    {
        Assert.False(CreateRegistry().SupportsCapability(default, GameCapability.Launch));
    }


    /// <summary>
    /// 未注册的供应商没有描述，此时放行由服务自己守门，避免误挡
    /// </summary>
    [Fact]
    public void SupportsCapability_UnknownProviderFallsThrough()
    {
        Assert.True(CreateRegistry().SupportsCapability(new GameKey("nonexistent", "game", "cn"), GameCapability.Install));
    }


    [Fact]
    public void GameCapabilityNotSupportedException_CarriesKeyAndCapability()
    {
        GameKey key = KuroGameMapping.WutheringWavesGlobal;
        var ex = new GameCapabilityNotSupportedException(key, GameCapability.Install);

        Assert.Equal(key, ex.Key);
        Assert.Equal(GameCapability.Install, ex.Capability);
        // 消息要能直接看出是哪款游戏缺少哪项能力
        Assert.Contains(key.ToString(), ex.Message, StringComparison.Ordinal);
        Assert.Contains("Install", ex.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<NotSupportedException>(ex);
    }


    /// <summary>
    /// 米哈游游戏必定能解析出 HoYoPlay 的启动器标识，其他供应商必定不能。
    /// 这是 HoYoPlayService 入口守门的判断依据。
    /// </summary>
    [Fact]
    public void LauncherId_ExistsOnlyForHoYoGames()
    {
        foreach (GameKey key in HoYoGameMapping.SupportedGameKeys)
        {
            GameBiz biz = HoYoGameMapping.ToGameBiz(key);
            Assert.NotNull(LauncherId.FromGameBiz(biz));
        }

        foreach (GameKey key in new[] { KuroGameMapping.WutheringWavesGlobal,
                                        HottaGameMapping.NevernessToEvernessTaiwan,
                                        GryphlineGameMapping.EndfieldDefault })
        {
            // 非米哈游的键根本转不成 GameBiz，自然也没有启动器标识
            Assert.False(HoYoGameMapping.TryToGameBiz(key, out _));
            Assert.Null(LauncherId.FromGameBiz(new GameBiz(key.ToString())));
        }
    }

}
