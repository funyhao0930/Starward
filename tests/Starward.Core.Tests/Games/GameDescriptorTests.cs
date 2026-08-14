using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Xunit;

namespace Starward.Core.Tests.Games;

/// <summary>
/// GameDescriptor 的能力判断
/// </summary>
public class GameDescriptorTests
{

    private static GameDescriptor Create(GameCapability capabilities) => new()
    {
        Key = new GameKey("kuro", "wuwa", "cn"),
        DisplayName = "Wuthering Waves",
        Capabilities = capabilities,
    };


    /// <summary>
    /// 只支持启动的非米哈游游戏
    /// </summary>
    [Fact]
    public void HasCapability_LaunchOnlyGame()
    {
        GameDescriptor descriptor = Create(GameCapability.Launch | GameCapability.Discovery
                                         | GameCapability.Screenshot | GameCapability.PlayTime);

        Assert.True(descriptor.HasCapability(GameCapability.Launch));
        Assert.True(descriptor.HasCapability(GameCapability.Discovery));
        Assert.True(descriptor.HasCapability(GameCapability.Screenshot));
        Assert.True(descriptor.HasCapability(GameCapability.PlayTime));

        Assert.False(descriptor.HasCapability(GameCapability.Gacha));
        Assert.False(descriptor.HasCapability(GameCapability.Install));
        Assert.False(descriptor.HasCapability(GameCapability.GameRecord));
    }


    /// <summary>
    /// 同时判断多个标志时，必须全部满足
    /// </summary>
    [Fact]
    public void HasCapability_RequiresEveryFlag()
    {
        GameDescriptor descriptor = Create(GameCapability.Launch | GameCapability.Screenshot);

        Assert.True(descriptor.HasCapability(GameCapability.Launch | GameCapability.Screenshot));
        Assert.False(descriptor.HasCapability(GameCapability.Launch | GameCapability.Gacha));
    }


    [Fact]
    public void HasCapability_NoneIsAlwaysFalse()
    {
        Assert.False(Create(GameCapability.Launch).HasCapability(GameCapability.None));
        Assert.False(Create(GameCapability.None).HasCapability(GameCapability.None));
        Assert.False(Create(GameCapability.None).HasCapability(GameCapability.Launch));
    }


    [Fact]
    public void HoYoDescriptors_ExposeLegacyGameBizForConfigCompatibility()
    {
        var catalog = new HoYoCatalogProvider();
        foreach (GameKey key in HoYoGameMapping.SupportedGameKeys)
        {
            GameDescriptor? descriptor = catalog.GetGame(key);
            Assert.NotNull(descriptor);
            Assert.Equal(HoYoGameMapping.ToGameBiz(key).Value, descriptor.LegacyGameBiz);
            Assert.False(string.IsNullOrWhiteSpace(descriptor.ProviderGameId));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.ExecutableName));
            Assert.True(descriptor.HasCapability(GameCapability.Launch));
        }
    }

}
