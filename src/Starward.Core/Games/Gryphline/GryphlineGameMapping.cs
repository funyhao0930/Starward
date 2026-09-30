namespace Starward.Core.Games.Gryphline;

/// <summary>
/// 鹰角网络（明日方舟：终末地），官方启动器为 GRYPHLINK。
/// <para/>
/// 安装、更新、修复都按文件比对线上最新整包的清单（见 <see cref="Launcher.Gryphline.GryphlineDownloadPlanner"/>），
/// 下载在 RPC 进程里完成。本地版本号文件是加密的，因此不声明 <see cref="GameCapability.VersionCheck"/>，
/// 要不要更新改比安装清单 game_files 的 MD5。
/// </summary>
public static class GryphlineGameMapping
{

    public const string ProviderId = "gryphline";


    /// <summary>
    /// 明日方舟：终末地
    /// </summary>
    public const string Endfield = "endfield";


    public static GameKey EndfieldDefault { get; } = new(ProviderId, Endfield, GameChannelIds.Default);


    /// <summary>
    /// GRYPHLINK 启动器在注册表卸载项中的显示名称。
    /// 键名是一段哈希，不固定，只能按显示名称匹配。
    /// </summary>
    public const string LauncherDisplayName = "GRYPHLINK";


    /// <summary>
    /// 游戏本体所在的子目录，相对于启动器安装根目录
    /// </summary>
    public const string GameFolderName = @"games\EndField Game";


    public static IReadOnlyList<GameKey> SupportedGameKeys { get; } = new[] { EndfieldDefault }.AsReadOnly();


    /// <summary>
    /// 本供应商支持的功能。
    /// 本地版本号文件加密，无法读取，因此没有 VersionCheck。
    /// </summary>
    public const GameCapability Capabilities = GameCapability.Launch
                                             | GameCapability.Discovery
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
                Key = EndfieldDefault,
                DisplayName = CoreLang.Game_Endfield,
                IconUri = "ms-appx:///Assets/Image/Transparent.png",
                ChannelIconUri = "ms-appx:///Assets/Image/Transparent.png",
                // 游戏选择器的卡片图，规格照米哈游的 416×234 缩略图加同尺寸透明 Logo。
                // 这家没有提供卡片图的接口，取自官网的开服主视觉（kv-obt-pc）与网页 SDK 的标题 SVG，放在 Images\Gryphline。
                ThumbnailUri = "ms-appx:///Images/Gryphline/Endfield_Thumbnail.jpg",
                LogoUri = "ms-appx:///Images/Gryphline/Endfield_Logo.png",
                // Unity 游戏，启动的就是游戏本体
                ExecutableName = Path.Combine(GameFolderName, "Endfield.exe"),
                ProcessName = "Endfield.exe",
                // 终末地默认用 DX12，与其他游戏相反，因此需要一个退回 DX11 的开关。
                // Unity 的写法是 -force-d3d11。
                DX11LaunchArgument = "-force-d3d11",
                ScreenshotPaths =
                [
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ENDFIELD"),
                ],
                // 没有 BackgroundPaths：官方启动器的背景图不落地，默认那张编在
                // Games.exe 的 Qt 资源里，取不出来。背景图走在线接口，
                // 见 GryphlineLauncherClient；接口不通时只能退到上面的截图。
                Capabilities = Capabilities,
            },
        }.AsReadOnly();
    }

}
