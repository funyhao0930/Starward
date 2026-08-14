namespace Starward.Core.Games.Gryphline;

/// <summary>
/// 鹰角网络（明日方舟：终末地），官方启动器为 GRYPHLINK。
/// <para/>
/// 本阶段只支持启动、搜索、截图与游玩时间，不实现下载与更新。
/// 本地版本号文件是加密的，因此不声明 <see cref="GameCapability.VersionCheck"/>。
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
    /// 本地版本号文件加密，无法读取，因此没有 VersionCheck；也没有实现下载器。
    /// </summary>
    public const GameCapability Capabilities = GameCapability.Launch
                                             | GameCapability.Discovery
                                             | GameCapability.Screenshot
                                             | GameCapability.PlayTime
                                             | GameCapability.Gacha;


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
                // Unity 游戏，启动的就是游戏本体
                ExecutableName = Path.Combine(GameFolderName, "Endfield.exe"),
                ProcessName = "Endfield.exe",
                ScreenshotPaths =
                [
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ENDFIELD"),
                ],
                Capabilities = Capabilities,
            },
        }.AsReadOnly();
    }

}
