namespace Starward.Core.Games.Manjuu;

/// <summary>
/// 蓝色星原：旅谣（Azur Promilia），蛮啾网络出品，海外由 JIMI PTE. LTD. 发行。
/// <para/>
/// 不实现下载与更新。启动相关的资料都来自本机官方启动器写下的注册表；
/// 背景图来自官方启动器缓存的 <c>AppData\data\cache.json</c>，见 <c>Starward.Core.Launcher.Manjuu</c>。
/// 官方启动器的接口都要用它自带的原生模块签名（密钥在加密的 <c>res\app_config.dat</c> 里），
/// 照其他厂商的惯例不把密钥搬进来，因此读不到官方公布的最新版本。
/// </summary>
public static class ManjuuGameMapping
{

    public const string ProviderId = "manjuu";


    /// <summary>
    /// 蓝色星原：旅谣
    /// </summary>
    public const string AzurPromilia = "promilia";


    /// <summary>
    /// 繁中服（台港澳），官方启动器的渠道是 C500
    /// </summary>
    public static GameKey AzurPromiliaTC { get; } = new(ProviderId, AzurPromilia, "tc");


    /// <summary>
    /// 繁中服的应用与渠道代号：43 是 appId 的前缀、C500 是渠道。
    /// 官方启动器写的注册表键名都由它拼成。
    /// </summary>
    private const string TCAppChannel = "43_C500";


    /// <summary>
    /// 繁中服在 HKLM 卸载项中的键名，InstallLocation 是官方启动器的根目录（如 D:\AzurPromilia_TC）
    /// </summary>
    public const string TCUninstallKey = $"Azurpromilia_TC_{TCAppChannel}";


    /// <summary>
    /// 官方启动器记录游戏本体的 HKCU 键，值有 Path（游戏目录）、Exe、Version 与 VersionName
    /// </summary>
    public const string TCLauncherRegistryKey = $@"Software\Azurpromilia_TC\{TCAppChannel}";


    /// <summary>
    /// <see cref="TCLauncherRegistryKey"/> 下的本地版本号，形如 <c>1.0.0.2646520</c>
    /// </summary>
    public const string LauncherVersionValueName = "Version";


    /// <summary>
    /// 游戏本体所在的子目录，相对于官方启动器的根目录
    /// </summary>
    public const string TCGameFolderName = "AzurPromilia_TC_game";


    /// <summary>
    /// 官方启动器（Electron）的数据目录，相对于根目录。
    /// 当期背景图与视频的地址记在其下的 <c>data\cache.json</c>。
    /// </summary>
    public const string LauncherDataFolderName = "AppData";


    /// <summary>
    /// 官方启动器启动游戏时带的参数。
    /// <para/>
    /// 来自官方接口 <c>/pc-launcher/client/gameversion/pre/check</c> 的 <c>launchParams</c>。
    /// 不带它直接运行 AzurPromilia.exe，游戏会弹「請使用啟動器啟動」并停在 SDK 初始化之前；
    /// 带上之后游戏自己的 SDK 会用上次登录的账号自动登录（pcsdk_log.txt 里的 auto_login），
    /// 不需要官方启动器在环境变量 manjuu_user_info / manjuu_channel_info 里传的东西
    /// （启动器没登录时 manjuu_user_info 本来就是空的）。
    /// </summary>
    public const string LaunchArgument = "-start=azurpromilia_launcher";


    /// <summary>
    /// 关闭 DLSS 的开关，官方启动器在玩家勾选「停用 DLSS」时附加
    /// </summary>
    public const string DisableDlssArgument = "-disableDLSS=1";


    /// <summary>
    /// Unity 存画面设置的位置 HKCU\Software\&lt;公司&gt;\&lt;产品&gt;，这里是公司那一层
    /// </summary>
    public const string UnityCompanyKeyPath = @"Software\manjuu";


    /// <summary>
    /// 从注册表里的版本字符串读出版本号，读不到返回 null
    /// </summary>
    public static Version? ParseVersion(string? text)
    {
        return Version.TryParse(text?.Trim(), out Version? version) ? version : null;
    }


    public static IReadOnlyList<GameKey> SupportedGameKeys { get; } = new[] { AzurPromiliaTC }.AsReadOnly();


    /// <summary>
    /// 本供应商支持的功能。没有实现下载器，因此不包含 Install、Update、Repair；
    /// 也没有抽卡记录与公告的公开来源。
    /// </summary>
    public const GameCapability Capabilities = GameCapability.Launch
                                             | GameCapability.Discovery
                                             | GameCapability.VersionCheck
                                             | GameCapability.Screenshot
                                             | GameCapability.PlayTime
                                             | GameCapability.GameSetting;


    public static IReadOnlyList<GameDescriptor> GetDescriptors()
    {
        return new[]
        {
            new GameDescriptor
            {
                Key = AzurPromiliaTC,
                DisplayName = CoreLang.Game_AzurPromilia,
                ChannelName = CoreLang.GameServer_TaiwanServer,
                IconUri = "ms-appx:///Assets/Image/Transparent.png",
                ChannelIconUri = "ms-appx:///Assets/Image/Transparent.png",
                // 游戏选择器的卡片图，规格照米哈游的 416×234 缩略图加同尺寸透明 Logo。
                // 卡片图取自官方启动器的动态背景视频（公测主视觉），Logo 是启动器缓存的白色 Logo，
                // 放在 Images\Manjuu。与其他游戏一样用最初的主视觉，不跟版本换。
                ThumbnailUri = "ms-appx:///Images/Manjuu/AzurPromilia_Thumbnail.jpg",
                LogoUri = "ms-appx:///Images/Manjuu/AzurPromilia_Logo.png",
                // 安装目录是官方启动器的根目录，游戏本体在其下
                ExecutableName = $@"{TCGameFolderName}\AzurPromilia.exe",
                LaunchArguments = LaunchArgument,
                // AzurPromilia.exe 一开就退出，再以系统管理员身份重新拉起同名的进程，
                // 因此即使文件名相同也要按进程名追踪，见 ManjuuGameLaunchProvider
                ProcessName = "AzurPromilia.exe",
                // 重新拉起时可能要等玩家在 UAC 提示里确认
                ProcessStartTimeout = TimeSpan.FromMinutes(2),
                DisableDlssLaunchArgument = DisableDlssArgument,
                // 官方接口 gameRuntimeParams.screenshot 给的是 {user}/Pictures/LSXY_PHOTO
                ScreenshotPaths =
                [
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "LSXY_PHOTO"),
                ],
                // 没有 BackgroundPaths：启动器的媒体缓存目录里同时放着背景、Logo 与扩展名是 .dat 的视频，
                // 按「最新的图片」去挑会挑到 Logo，背景改由 ManjuuBackgroundProvider 按 cache.json 指名取用
                Capabilities = Capabilities,
            },
        }.AsReadOnly();
    }

}
