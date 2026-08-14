using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Core.Games.HoYo;
using Starward.Core.Games.Hotta;
using Starward.Core.Games.Kuro;
using Starward.Core.Tests.Games.Fakes;
using Xunit;

namespace Starward.Core.Tests.Games;

/// <summary>
/// 只支持启动的三款非米哈游游戏。
/// 全部资料都来自本机官方启动器目录与注册表，没有调用任何在线接口。
/// </summary>
public class NewGameProviderTests
{

    public static TheoryData<GameKey, string, string, GameCapability> Games => new()
    {
        {
            KuroGameMapping.WutheringWavesGlobal,
            @"Wuthering Waves Game\Wuthering Waves.exe",
            "Client-Win64-Shipping",
            KuroGameMapping.Capabilities
        },
        {
            HottaGameMapping.NevernessToEvernessTaiwan,
            @"NTETW\NTETWGame.exe",
            "HTGame",
            HottaGameMapping.Capabilities
        },
        {
            GryphlineGameMapping.EndfieldDefault,
            @"games\EndField Game\Endfield.exe",
            "Endfield",
            GryphlineGameMapping.Capabilities
        },
    };


    private static IReadOnlyList<GameDescriptor> AllDescriptors() =>
    [
        .. KuroGameMapping.GetDescriptors(),
        .. HottaGameMapping.GetDescriptors(),
        .. GryphlineGameMapping.GetDescriptors(),
    ];


    [Theory]
    [MemberData(nameof(Games))]
    public void Descriptor_HasExpectedExecutableProcessAndCapabilities(GameKey key, string exe, string processName, GameCapability capabilities)
    {
        GameDescriptor descriptor = AllDescriptors().First(x => x.Key == key);
        Assert.Equal(exe, descriptor.ExecutableName);
        Assert.Equal(processName, descriptor.ProcessNameWithoutExtension);
        Assert.Equal(capabilities, descriptor.Capabilities);
    }


    /// <summary>
    /// 本阶段不实现下载器，三款游戏都不能声明安装、更新、修复
    /// </summary>
    [Theory]
    [MemberData(nameof(Games))]
    public void Descriptor_DoesNotClaimDownloadCapabilities(GameKey key, string exe, string processName, GameCapability capabilities)
    {
        _ = exe;
        _ = processName;
        _ = capabilities;
        GameDescriptor descriptor = AllDescriptors().First(x => x.Key == key);
        Assert.False(descriptor.HasCapability(GameCapability.Install));
        Assert.False(descriptor.HasCapability(GameCapability.Update));
        Assert.False(descriptor.HasCapability(GameCapability.Repair));
        // 抽卡、游戏记录等米哈游专属功能也不能声明
        Assert.False(descriptor.HasCapability(GameCapability.Gacha));
        Assert.False(descriptor.HasCapability(GameCapability.GameRecord));
        Assert.False(descriptor.HasCapability(GameCapability.CloudGame));
    }


    /// <summary>
    /// 最小可用的能力组合
    /// </summary>
    [Theory]
    [MemberData(nameof(Games))]
    public void Descriptor_SupportsLaunchDiscoveryScreenshotAndPlayTime(GameKey key, string exe, string processName, GameCapability capabilities)
    {
        _ = exe;
        _ = processName;
        _ = capabilities;
        GameDescriptor descriptor = AllDescriptors().First(x => x.Key == key);
        Assert.True(descriptor.HasCapability(GameCapability.Launch
                                           | GameCapability.Discovery
                                           | GameCapability.Screenshot
                                           | GameCapability.PlayTime));
    }


    /// <summary>
    /// 终末地的本地版本号文件是加密的，因此不能声明版本检查
    /// </summary>
    [Fact]
    public void Endfield_DoesNotSupportVersionCheck()
    {
        GameDescriptor descriptor = GryphlineGameMapping.GetDescriptors()[0];
        Assert.False(descriptor.HasCapability(GameCapability.VersionCheck));
    }


    [Fact]
    public void WutheringWavesAndNeverness_SupportVersionCheck()
    {
        Assert.True(KuroGameMapping.GetDescriptors()[0].HasCapability(GameCapability.VersionCheck));
        Assert.True(HottaGameMapping.GetDescriptors()[0].HasCapability(GameCapability.VersionCheck));
    }


    /// <summary>
    /// 鸣潮与终末地不需要额外的启动参数。
    /// 鸣潮启动的 Wuthering Waves.exe 是游戏自己的引导程序，官方快捷方式也指向它。
    /// </summary>
    [Fact]
    public void WutheringWavesAndEndfield_NeedNoExtraLaunchArguments()
    {
        Assert.Null(KuroGameMapping.GetDescriptors()[0].LaunchArguments);
        Assert.Null(GryphlineGameMapping.GetDescriptors()[0].LaunchArguments);
    }


    /// <summary>
    /// 异环必须经过官方登录外壳启动。
    /// <para/>
    /// 直接启动 HTGame.exe 能进游戏，但只会是国际服：区服由外壳通过
    /// 共享内存握手传给游戏，游戏本体没有任何区服相关的命令行开关。
    /// 因此启动的是外壳，而游玩时间按它拉起的游戏本体计算。
    /// </summary>
    [Fact]
    public void Neverness_LaunchesThroughTheOfficialShell()
    {
        GameDescriptor descriptor = HottaGameMapping.GetDescriptors()[0];
        Assert.Equal("NTETWGame.exe", Path.GetFileName(descriptor.ExecutableName));
        Assert.Equal("/launcher", descriptor.LaunchArguments);
        Assert.Equal("HTGame.exe", descriptor.ProcessName);
        Assert.Equal("tw", descriptor.Key.ChannelId);
    }


    /// <summary>
    /// 外壳与游戏本体是两个进程，必须按进程名而不是进程 ID 记录游玩时间
    /// </summary>
    [Fact]
    public async Task Neverness_TracksPlayTimeByProcessName()
    {
        string root = Path.Combine(Path.GetTempPath(), "StarwardTests", Guid.NewGuid().ToString("N"));
        try
        {
            GameDescriptor descriptor = HottaGameMapping.GetDescriptors()[0];
            string exe = Path.Combine(root, descriptor.ExecutableName!);
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "");

            var catalog = new SimpleGameCatalogProvider(HottaGameMapping.ProviderId, HottaGameMapping.GetDescriptors);
            var provider = new SimpleGameLaunchProvider(HottaGameMapping.ProviderId, catalog, new FakeGameLaunchSettings());
            GameLaunchCommand command = await provider.CreateLaunchCommandAsync(
                descriptor.Key,
                new GameLaunchOptions { InstallPath = root },
                TestContext.Current.CancellationToken);

            Assert.True(command.TrackByProcessName);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }


    /// <summary>
    /// 非米哈游游戏没有 GameBiz，使用 GameKey 的正规字符串作为存储键
    /// </summary>
    [Theory]
    [MemberData(nameof(Games))]
    public void Descriptor_UsesGameKeyAsSettingsKey(GameKey key, string exe, string processName, GameCapability capabilities)
    {
        _ = exe;
        _ = processName;
        _ = capabilities;
        GameDescriptor descriptor = AllDescriptors().First(x => x.Key == key);
        Assert.Null(descriptor.LegacyGameBiz);
        Assert.Equal(key.ToString(), descriptor.SettingsKey);
        Assert.Contains(":", descriptor.SettingsKey, StringComparison.Ordinal);
    }


    /// <summary>
    /// 米哈游游戏的存储键必须仍是旧的 GameBiz 字符串，否则用户既有配置会失效
    /// </summary>
    [Fact]
    public void HoYoDescriptors_KeepLegacyGameBizAsSettingsKey()
    {
        foreach (GameKey key in HoYoGameMapping.SupportedGameKeys)
        {
            GameDescriptor descriptor = new HoYoCatalogProvider().GetGame(key)!;
            Assert.Equal(HoYoGameMapping.ToGameBiz(key).Value, descriptor.SettingsKey);
            Assert.DoesNotContain(":", descriptor.SettingsKey, StringComparison.Ordinal);
        }
    }


    /// <summary>
    /// 拿不到描述对象的地方（例如读写启动设置）必须算出与描述对象相同的键，
    /// 否则非米哈游游戏的自定义启动参数等设置会被静默忽略。
    /// </summary>
    [Fact]
    public void ToSettingsKey_MatchesDescriptorSettingsKey()
    {
        foreach (GameDescriptor descriptor in AllDescriptors())
        {
            Assert.Equal(descriptor.SettingsKey, GameKeyResolver.ToSettingsKey(descriptor.Key));
        }
        foreach (GameKey key in HoYoGameMapping.SupportedGameKeys)
        {
            GameDescriptor descriptor = new HoYoCatalogProvider().GetGame(key)!;
            Assert.Equal(descriptor.SettingsKey, GameKeyResolver.ToSettingsKey(key));
        }
    }


    /// <summary>
    /// 存储键必须唯一，否则两款游戏会互相覆盖配置
    /// </summary>
    [Fact]
    public void SettingsKeys_AreUniqueAcrossProviders()
    {
        List<string> keys =
        [
            .. new HoYoCatalogProvider().GetGames().Select(x => x.SettingsKey),
            .. AllDescriptors().Select(x => x.SettingsKey),
        ];
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }


    [Theory]
    [MemberData(nameof(Games))]
    public void GameKeyResolver_RoundTripsSettingsKey(GameKey key, string exe, string processName, GameCapability capabilities)
    {
        _ = exe;
        _ = processName;
        _ = capabilities;
        GameDescriptor descriptor = AllDescriptors().First(x => x.Key == key);
        Assert.True(GameKeyResolver.TryResolve(descriptor.SettingsKey, out GameKey resolved));
        Assert.Equal(key, resolved);
    }


    [Fact]
    public void GameKeyResolver_StillResolvesLegacyGameBiz()
    {
        Assert.True(GameKeyResolver.TryResolve("hk4e_cn", out GameKey key));
        Assert.Equal(HoYoGameMapping.FromGameBiz(GameBiz.hk4e_cn), key);
    }


    [Fact]
    public void GameKeyResolver_ReturnsFalseForGarbage()
    {
        Assert.False(GameKeyResolver.TryResolve("", out _));
        Assert.False(GameKeyResolver.TryResolve((string?)null, out _));
        Assert.False(GameKeyResolver.TryResolve("nonsense", out _));
    }


    /// <summary>
    /// 三款游戏的截图目录：鸣潮在安装目录下，另外两款在用户的图片文件夹中
    /// </summary>
    [Fact]
    public void ScreenshotPaths_SupportBothRelativeAndAbsolute()
    {
        GameDescriptor wuwa = KuroGameMapping.GetDescriptors()[0];
        string relative = Assert.Single(wuwa.ScreenshotPaths);
        Assert.False(Path.IsPathFullyQualified(relative));

        foreach (GameDescriptor descriptor in new[] { HottaGameMapping.GetDescriptors()[0], GryphlineGameMapping.GetDescriptors()[0] })
        {
            string absolute = Assert.Single(descriptor.ScreenshotPaths);
            Assert.True(Path.IsPathFullyQualified(absolute));
        }
    }


    /// <summary>
    /// 通用的启动 Provider 能为这三款游戏构建出正确的启动命令
    /// </summary>
    [Fact]
    public async Task SimpleGameLaunchProvider_BuildsCommandForNeverness()
    {
        string root = Path.Combine(Path.GetTempPath(), "StarwardTests", Guid.NewGuid().ToString("N"));
        try
        {
            GameDescriptor descriptor = HottaGameMapping.GetDescriptors()[0];
            string exe = Path.Combine(root, descriptor.ExecutableName!);
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "");

            var catalog = new SimpleGameCatalogProvider(HottaGameMapping.ProviderId, HottaGameMapping.GetDescriptors);
            var provider = new SimpleGameLaunchProvider(HottaGameMapping.ProviderId, catalog, new FakeGameLaunchSettings());

            GameLaunchCommand command = await provider.CreateLaunchCommandAsync(
                descriptor.Key,
                new GameLaunchOptions { InstallPath = root },
                TestContext.Current.CancellationToken);

            Assert.Equal(exe, command.FileName);
            Assert.Equal("/launcher", command.Arguments);
            Assert.True(command.TrackByProcessName);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }


    [Fact]
    public async Task SimpleGameLaunchProvider_AppendsUserArgumentsAfterGameArguments()
    {
        string root = Path.Combine(Path.GetTempPath(), "StarwardTests", Guid.NewGuid().ToString("N"));
        try
        {
            GameDescriptor descriptor = HottaGameMapping.GetDescriptors()[0];
            string exe = Path.Combine(root, descriptor.ExecutableName!);
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "");

            var catalog = new SimpleGameCatalogProvider(HottaGameMapping.ProviderId, HottaGameMapping.GetDescriptors);
            var settings = new FakeGameLaunchSettings { StartArgument = "-custom", UsePopupWindow = true };
            var provider = new SimpleGameLaunchProvider(HottaGameMapping.ProviderId, catalog, settings);

            GameLaunchCommand command = await provider.CreateLaunchCommandAsync(
                descriptor.Key,
                new GameLaunchOptions { InstallPath = root },
                TestContext.Current.CancellationToken);

            // 游戏本身的参数在前，用户自定义的参数在后
            Assert.Equal("/launcher -custom -popupwindow", command.Arguments);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }


    [Fact]
    public void SimpleGameCatalogProvider_OnlyAnswersForItsOwnProvider()
    {
        var catalog = new SimpleGameCatalogProvider(KuroGameMapping.ProviderId, KuroGameMapping.GetDescriptors);
        Assert.NotNull(catalog.GetGame(KuroGameMapping.WutheringWavesGlobal));
        Assert.Null(catalog.GetGame(HottaGameMapping.NevernessToEvernessTaiwan));
        Assert.Null(catalog.GetGame(HoYoGameMapping.FromGameBiz(GameBiz.hk4e_cn)));
    }


    /// <summary>
    /// 三款游戏加入注册表后，米哈游游戏的清单不受影响
    /// </summary>
    [Fact]
    public void Registry_MergesAllProvidersWithoutAffectingHoYo()
    {
        var registry = new GameProviderRegistry(
        [
            new HoYoCatalogProvider(),
            new SimpleGameCatalogProvider(KuroGameMapping.ProviderId, KuroGameMapping.GetDescriptors),
            new SimpleGameCatalogProvider(HottaGameMapping.ProviderId, HottaGameMapping.GetDescriptors),
            new SimpleGameCatalogProvider(GryphlineGameMapping.ProviderId, GryphlineGameMapping.GetDescriptors),
        ], [], []);

        IReadOnlyList<GameDescriptor> games = registry.GetAllGames();
        Assert.Equal(HoYoGameMapping.SupportedGameKeys.Count + 3, games.Count);
        Assert.Equal(HoYoGameMapping.SupportedGameKeys.Count, games.Count(x => x.Key.IsProvider(GameProviderIds.HoYo)));
        Assert.NotNull(registry.GetGame(KuroGameMapping.WutheringWavesGlobal));
        Assert.NotNull(registry.GetGame(HoYoGameMapping.FromGameBiz(GameBiz.nap_bilibili)));
    }

}
