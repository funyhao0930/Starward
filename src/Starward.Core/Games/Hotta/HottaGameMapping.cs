namespace Starward.Core.Games.Hotta;

/// <summary>
/// 异环（Neverness to Everness）。
/// <para/>
/// 本阶段只支持启动、搜索、截图与游玩时间，不实现下载与更新。
/// 所有资料都来自本机官方启动器的 Config.ini 与注册表，没有调用任何在线接口。
/// </summary>
public static class HottaGameMapping
{

    public const string ProviderId = "hotta";


    /// <summary>
    /// 异环
    /// </summary>
    public const string NevernessToEverness = "nte";


    /// <summary>
    /// 台服，由 Iwplay 代理
    /// </summary>
    public static GameKey NevernessToEvernessTaiwan { get; } = new(ProviderId, NevernessToEverness, "tw");


    /// <summary>
    /// 台服在注册表卸载项中的键名，由官方启动器 Config.ini 的 [Global] reg 指定
    /// </summary>
    public const string TaiwanUninstallKey = "NTEGAT";


    /// <summary>
    /// 官方启动器的配置文件，相对于游戏安装根目录。
    /// 其中的 [VERSION] Version 是本地版本号。
    /// </summary>
    public const string ConfigFileRelativePath = @"NTETW\Config\Config.ini";


    public static IReadOnlyList<GameKey> SupportedGameKeys { get; } = new[] { NevernessToEvernessTaiwan }.AsReadOnly();


    /// <summary>
    /// 本供应商支持的功能。没有实现下载器，因此不包含 Install、Update、Repair。
    /// </summary>
    public const GameCapability Capabilities = GameCapability.Launch
                                             | GameCapability.Discovery
                                             | GameCapability.VersionCheck
                                             | GameCapability.Screenshot
                                             | GameCapability.PlayTime;


    public static IReadOnlyList<GameDescriptor> GetDescriptors()
    {
        return new[]
        {
            new GameDescriptor
            {
                Key = NevernessToEvernessTaiwan,
                DisplayName = CoreLang.Game_NevernessToEverness,
                ChannelName = CoreLang.GameServer_TaiwanServer,
                IconUri = "ms-appx:///Assets/Image/Transparent.png",
                ChannelIconUri = "ms-appx:///Assets/Image/Transparent.png",
                // Config.ini 的 [UPDATE_CONFIG] LaunchFilePath 与 LaunchCmdLine
                ExecutableName = @"NTETW\NTETWGame.exe",
                LaunchArguments = "/launcher",
                // 启动的是外壳，游戏本体是虚幻引擎的 HTGame.exe
                ProcessName = "HTGame.exe",
                ScreenshotPaths =
                [
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "NevernessToEverness"),
                ],
                Capabilities = Capabilities,
            },
        }.AsReadOnly();
    }

}
