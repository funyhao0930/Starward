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
    /// 台服的存档目录后缀，对应游戏本体的 <c>-saveddirsuffix=</c> 开关。
    /// <para/>
    /// 注意：它只决定存档与配置目录（Saved_GAT），并不决定区服。
    /// 实测传入本值后游戏确实使用 Saved_GAT，但界面仍是国际服。
    /// 区服由官方登录外壳通过共享内存握手传给游戏
    /// （启动器中可见 <c>Global\ArcGame_ShareMem_%1_%2</c> 与
    /// <c>GameShareMemMgr::setGameStartInfo</c>），
    /// 游戏本体没有任何区服相关的命令行开关，因此无法绕开外壳直接进入台服。
    /// </summary>
    public const string TaiwanSavedDirSuffix = "GAT";


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
                // 直接启动虚幻引擎的游戏本体。
                // Config.ini 记录的 NTETWGame.exe /launcher 是官方的登录外壳，
                // 走那条路只会打开官方启动器，与本程序替代启动器的目的相悖。
                // 必须经过官方登录外壳：区服由它通过共享内存交给游戏，
                // 直接启动 HTGame.exe 可以进入游戏，但只会是国际服。
                // /launcher 来自官方 Config.ini 的 [UPDATE_CONFIG] LaunchCmdLine，
                // 走这里可以跳过官方的更新器。
                ExecutableName = @"NTETW\NTETWGame.exe",
                LaunchArguments = "/launcher",
                // 外壳最终拉起的才是游戏本体，游玩时间要按它计算
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
