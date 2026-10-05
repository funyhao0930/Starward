namespace Starward.Core.Games.Kuro;

/// <summary>
/// 库洛游戏（鸣潮）。
/// <para/>
/// 安装、更新、修复与预下载走官方启动器同一套在线配置（见 <see cref="Launcher.Kuro.KuroLauncherClient"/>），
/// 下载在 RPC 进程里完成；搜索、截图与游玩时间仍只读本机的官方启动器目录与注册表。
/// 启动参数要看装了哪几档资源，见 <see cref="KuroGameLaunchProvider"/>。
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


    /// <summary>
    /// 虚幻引擎的画面设置，相对于启动器安装根目录
    /// </summary>
    public static readonly string GameUserSettingsRelativePath = Path.Combine(GameFolderName, @"Client\Saved\Config\WindowsNoEditor\GameUserSettings.ini");


    /// <summary>
    /// Engine.ini、Input.ini 所在的目录，相对于启动器安装根目录
    /// </summary>
    public static readonly string SavedConfigRelativePath = Path.Combine(GameFolderName, @"Client\Saved\Config\WindowsNoEditor");


    /// <summary>
    /// 旧版调校教程让人放的 UserEngine.ini。它在 Saved 里的 Engine.ini 之后载入，
    /// 会盖掉那边的设置，所以存在时要提醒玩家停用。
    /// </summary>
    public static readonly string UserEngineIniRelativePath = Path.Combine(GameFolderName, @"Client\Config\UserEngine.ini");


    /// <summary>
    /// 游戏 SDK 带的文鼎方新书 H7，游戏内公告板的字就是这一套，相对于启动器安装根目录。
    /// 只在本机读来显示，不随 Starward 分发。
    /// </summary>
    public static readonly string NoticeFontRelativePath = Path.Combine(GameFolderName, @"Client\Binaries\Win64\ThirdParty\KrPcSdk_Global\H7GBKHeavy.TTF");


    public static IReadOnlyList<GameKey> SupportedGameKeys { get; } = new[] { WutheringWavesGlobal }.AsReadOnly();


    /// <summary>
    /// 本供应商支持的功能
    /// </summary>
    public const GameCapability Capabilities = GameCapability.Launch
                                             | GameCapability.Discovery
                                             | GameCapability.VersionCheck
                                             | GameCapability.Install
                                             | GameCapability.Update
                                             | GameCapability.Repair
                                             | GameCapability.Screenshot
                                             | GameCapability.PlayTime
                                             | GameCapability.Gacha
                                             | GameCapability.Announcement
                                             | GameCapability.GameSetting;


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
                // 游戏选择器的卡片图，规格照米哈游的 416×234 缩略图加同尺寸透明 Logo。
                // 这家没有提供卡片图的接口，取自官方启动器前端包 krfeapp.dat 自带的主视觉与繁中 Logo，放在 Images\Kuro。
                ThumbnailUri = "ms-appx:///Images/Kuro/WuWa_Thumbnail.jpg",
                LogoUri = "ms-appx:///Images/Kuro/WuWa_Logo.png",
                // 启动器安装根目录下的一层外壳，实际运行的是虚幻引擎的 Shipping 进程
                ExecutableName = Path.Combine(GameFolderName, "Wuthering Waves.exe"),
                ProcessName = "Client-Win64-Shipping.exe",
                // 官方启动器自己提供的两个开关，取自在线配置的 RHIOptionList 与 commandList，
                // 说明文字是「游戏异常时选择」。虚幻的写法是 -dx11，不是 Unity 的 -force-d3d11。
                // 没有固定参数：3.7.0 起必须带的 -krqlv=（资源分级）要看本机装了哪几档，
                // 由 KuroGameLaunchProvider 在启动时决定，见 KuroResourceTier。
                DX11LaunchArgument = "-dx11",
                DisableDlssLaunchArgument = "-slno",
                ScreenshotPaths = [Path.Combine(GameFolderName, @"Client\Saved\ScreenShot")],
                // 背景图正常走官方的在线接口（见 KuroLauncherClient），这里只是接口不通时的兜底。
                // 官方启动器把主界面动态背景缓存为 kr_game_cache\animate_bg\<md5>\home_*.jpg 逐帧序列
                // （animate_bg.json 记录 12 秒、20fps），指到上层递归即可命中。
                // 注意这份缓存只有官方启动器跑过才会有，而且不随游戏版本更新，可能停在很旧的版本上。
                BackgroundPaths = [@"kr_game_cache\animate_bg"],
                Capabilities = Capabilities,
            },
        }.AsReadOnly();
    }

}
