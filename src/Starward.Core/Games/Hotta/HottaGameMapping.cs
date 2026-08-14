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
    /// 台服的存档目录后缀。
    /// <para/>
    /// 游戏本体支持 <c>-saveddirsuffix=</c>（该字符串存在于 HTGame.exe 中），
    /// 官方登录外壳正是用它区分区服：不传时用 Saved 目录并显示国际服界面，
    /// 传入本值时用 Saved_GAT 目录并显示台服界面。
    /// GAT 是台服的构建代号，可在启动器的 PDB 路径 PGP_HD_DHYH_GAT 中看到。
    /// </summary>
    public const string TaiwanSavedDirSuffix = "_GAT";


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
                ExecutableName = @"Client\WindowsNoEditor\HT\Binaries\Win64\HTGame.exe",
                // 不带这个参数会进入国际服界面
                LaunchArguments = $"-saveddirsuffix={TaiwanSavedDirSuffix}",
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
