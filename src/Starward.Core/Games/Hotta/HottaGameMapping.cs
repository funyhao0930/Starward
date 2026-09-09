using System.Text.RegularExpressions;

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
    /// 台服的存档目录后缀，对应游戏本体的 <c>--saveddirsuffix=</c> 开关。
    /// <para/>
    /// 官方外壳的完整启动命令记录在 <c>NTETW\UserData\Log\NTETWGame.log</c> 里：
    /// <code>HTGame.exe /Game/LoginAndCreate/Map/Updater/Updater_P --saveddirsuffix=GAT -SAVEWINPOS=1</code>
    /// <para/>
    /// 注意：它只决定存档与配置目录（Saved_GAT），并不决定区服。
    /// 实测传入本值后游戏确实使用 Saved_GAT，但界面仍是国际服，
    /// 原因见 <see cref="GetDescriptors"/> 里对启动方式的说明。
    /// </summary>
    public const string TaiwanSavedDirSuffix = "GAT";


    /// <summary>
    /// 官方启动器的配置文件，相对于游戏安装根目录。
    /// 其中的 [VERSION] Version 是本地版本号。
    /// </summary>
    public const string ConfigFileRelativePath = @"NTETW\Config\Config.ini";


    /// <summary>
    /// <c>[VERSION] Version=1.0.8.0727</c>，本机与官方公布的版本文件都是这个写法
    /// </summary>
    private static readonly Regex VersionRegex = new(@"(?m)^[ \t]*Version[ \t]*=[ \t]*(.+)$", RegexOptions.Compiled);


    /// <summary>
    /// 官方公布版本号的地址，由游戏自己的 Config.ini 给出：
    /// <c>[VERSION] VersionInfoFileURL</c> 与 <c>[UPDATE_CONFIG] BackupVersionURL</c>。
    /// </summary>
    private static readonly Regex VersionInfoUrlRegex = new(@"(?m)^[ \t]*(?:VersionInfoFileURL|BackupVersionURL)[ \t]*=[ \t]*(https://\S+)[ \t]*$", RegexOptions.Compiled);


    /// <summary>
    /// 从 ini 文本中读出版本号，读不到返回 null
    /// </summary>
    public static Version? ParseVersion(string? iniText)
    {
        if (string.IsNullOrEmpty(iniText))
        {
            return null;
        }
        Match match = VersionRegex.Match(iniText);
        return match.Success && Version.TryParse(match.Groups[1].Value.Trim(), out Version? version) ? version : null;
    }


    /// <summary>
    /// 从游戏的 Config.ini 中读出官方版本文件的地址，主站在前、备援在后。
    /// <para/>
    /// 地址不写死在代码里：它是游戏自己配置的，换了代理商或域名也不必改这里。
    /// </summary>
    public static IReadOnlyList<string> ParseVersionInfoUrls(string? configText)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(configText))
        {
            return urls.AsReadOnly();
        }
        foreach (Match match in VersionInfoUrlRegex.Matches(configText))
        {
            string url = match.Groups[1].Value.Trim();
            if (!urls.Contains(url, StringComparer.OrdinalIgnoreCase))
            {
                urls.Add(url);
            }
        }
        return urls.AsReadOnly();
    }


    /// <summary>
    /// 虚幻引擎的画面设置。异环不写在游戏目录里，而在用户目录下，
    /// 中间那层与 <see cref="TaiwanSavedDirSuffix"/> 对应（台服是 Saved_GAT）。
    /// </summary>
    public static string GetGameUserSettingsPath(GameKey key)
    {
        if (key != NevernessToEvernessTaiwan)
        {
            return "";
        }
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "HT", $"Saved_{TaiwanSavedDirSuffix}", @"Config\Windows\GameUserSettings.ini");
    }


    public static IReadOnlyList<GameKey> SupportedGameKeys { get; } = new[] { NevernessToEvernessTaiwan }.AsReadOnly();


    /// <summary>
    /// 本供应商支持的功能。没有实现下载器，因此不包含 Install、Update、Repair。
    /// <para/>
    /// 声明 <see cref="GameCapability.Gacha"/> 说的是「这款游戏有抽卡记录」，
    /// 不是「Starward 已经抓得到」。异环没有抽卡记录接口，
    /// 记录只走游戏自己的 RPC，所以抽卡页只能导入第三方抓包工具 nte-exporter 的文件，
    /// 详见 <c>Starward.Core.Gacha.Hotta.HottaGachaClient</c>。
    /// </summary>
    public const GameCapability Capabilities = GameCapability.Launch
                                             | GameCapability.Discovery
                                             | GameCapability.VersionCheck
                                             | GameCapability.Screenshot
                                             | GameCapability.PlayTime
                                             | GameCapability.Gacha
                                             | GameCapability.GameSetting;


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
                // 只能启动官方外壳 NTETWGame.exe，不能像其他游戏那样直接拉起游戏本体。
                // 外壳不只是启动器，它同时是账号登录的服务端：
                // 游戏侧的 WPLauncherSDK_64.dll 读同目录的 GameLauncher.config
                // （内容是外壳目录下 WPGameClientSDK_64.dll 的绝对路径），
                // 加载它之后通过命名管道 \\.\PIPE\ 连回外壳，
                // 再共用 Global\ArcGame_ShareMem_ 取得启动参数。
                // 登录窗口是外壳画的 QML（日志里的 globalquickloginframe.qml），
                // 拿到 token 后经管道回传给游戏，游戏侧除了 SteamLogin 没有任何自带的登录入口。
                // 因此绕开外壳直接跑 HTGame.exe 只能进国际服，
                // 要做到直接启动就得自己实现代理商的登录协议，超出本项目范围。
                // /launcher 来自官方 Config.ini 的 [UPDATE_CONFIG] LaunchCmdLine，
                // 走这里可以跳过官方的更新器。
                ExecutableName = @"NTETW\NTETWGame.exe",
                LaunchArguments = "/launcher",
                // 外壳最终拉起的才是游戏本体，游玩时间要按它计算
                ProcessName = "HTGame.exe",
                // 要先在外壳里登录，游戏进程可能几分钟后才出现
                ProcessStartTimeout = TimeSpan.FromMinutes(10),
                ScreenshotPaths =
                [
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "NevernessToEverness"),
                ],
                // 官方启动器自带的背景图放在这里（bgimgs\bg_0.jpg）。
                // 中间一层是 Config.ini 的 GameID，随版本可能变；
                // 这里按需求直接指到 bgimgs，若日后更新更动了 GameID 层级，需同步调整。
                BackgroundPaths = [@"NTETW\ResFilesM\2000013\bgimgs"],
                Capabilities = Capabilities,
            },
        }.AsReadOnly();
    }

}
