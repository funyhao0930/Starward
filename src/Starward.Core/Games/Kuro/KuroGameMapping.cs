namespace Starward.Core.Games.Kuro;

/// <summary>
/// 库洛游戏（鸣潮）。
/// <para/>
/// 本阶段只支持启动、搜索、截图与游玩时间，不实现下载与更新，
/// 所有资料都来自本机的官方启动器目录与注册表，没有调用任何在线接口。
/// </summary>
public static class KuroGameMapping
{

    public const string ProviderId = "kuro";


    /// <summary>
    /// 鸣潮
    /// </summary>
    public const string WutheringWaves = "wuwa";


    /// <summary>
    /// 国际服，对应官方启动器的 appId 50004
    /// </summary>
    public static GameKey WutheringWavesGlobal { get; } = new(ProviderId, WutheringWaves, GameChannelIds.Global);


    /// <summary>
    /// 官方启动器注册表中的 appId，用于定位安装路径
    /// </summary>
    public const string GlobalAppId = "50004";


    /// <summary>
    /// 游戏本体所在的子目录，相对于启动器安装根目录
    /// </summary>
    public const string GameFolderName = "Wuthering Waves Game";


    /// <summary>
    /// 记录本地版本号的文件，相对于游戏目录
    /// </summary>
    public const string VersionFileName = "launcherDownloadConfig.json";


    public static IReadOnlyList<GameKey> SupportedGameKeys { get; } = new[] { WutheringWavesGlobal }.AsReadOnly();


    /// <summary>
    /// 本供应商支持的功能。没有实现下载器，因此不包含 Install、Update、Repair。
    /// </summary>
    public const GameCapability Capabilities = GameCapability.Launch
                                             | GameCapability.Discovery
                                             | GameCapability.VersionCheck
                                             | GameCapability.Screenshot
                                             | GameCapability.PlayTime
                                             | GameCapability.Gacha;


    public static IReadOnlyList<GameDescriptor> GetDescriptors()
    {
        return new[]
        {
            new GameDescriptor
            {
                Key = WutheringWavesGlobal,
                DisplayName = CoreLang.Game_WutheringWaves,
                ChannelName = CoreLang.GameServer_GlobalServer,
                IconUri = "ms-appx:///Assets/Image/Transparent.png",
                ChannelIconUri = "ms-appx:///Assets/Image/Transparent.png",
                // 启动器安装根目录下的一层外壳，实际运行的是虚幻引擎的 Shipping 进程
                ExecutableName = Path.Combine(GameFolderName, "Wuthering Waves.exe"),
                ProcessName = "Client-Win64-Shipping.exe",
                ScreenshotPaths = [Path.Combine(GameFolderName, @"Client\Saved\ScreenShot")],
                Capabilities = Capabilities,
            },
        }.AsReadOnly();
    }

}
