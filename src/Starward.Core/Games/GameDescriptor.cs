namespace Starward.Core.Games;

/// <summary>
/// 一款游戏（含渠道）的展示信息与能力描述，由 <see cref="IGameCatalogProvider"/> 提供。
/// </summary>
public sealed record GameDescriptor
{

    /// <summary>
    /// 通用游戏标识
    /// </summary>
    public required GameKey Key { get; init; }


    /// <summary>
    /// 游戏名称，已本地化
    /// </summary>
    public required string DisplayName { get; init; }


    /// <summary>
    /// 渠道名称，已本地化，例如国服、国际服
    /// </summary>
    public string? ChannelName { get; init; }


    /// <summary>
    /// 游戏图标，可以是 ms-appx:/// 或 http(s):// 链接
    /// </summary>
    public string? IconUri { get; init; }


    /// <summary>
    /// 渠道图标，可以是 ms-appx:/// 或 http(s):// 链接
    /// </summary>
    public string? ChannelIconUri { get; init; }


    /// <summary>
    /// 游戏选择器中的缩略图
    /// </summary>
    public string? ThumbnailUri { get; init; }


    /// <summary>
    /// 游戏选择器中的 Logo
    /// </summary>
    public string? LogoUri { get; init; }


    /// <summary>
    /// 供应商内部的游戏标识，对通用代码是不透明的句柄，
    /// 只用于回传给同一个供应商（例如 HoYoPlay 的远程 GameId）。
    /// 通用代码不应解析其内容。
    /// </summary>
    public string? ProviderGameId { get; init; }


    /// <summary>
    /// 启动时执行的文件名，带 .exe 扩展名。为空时需要供应商在线查询。
    /// </summary>
    public string? ExecutableName { get; init; }


    /// <summary>
    /// 游戏进程名，带 .exe 扩展名。
    /// 部分游戏启动的是一层外壳（例如虚幻引擎的 Wuthering Waves.exe 实际运行
    /// Client-Win64-Shipping.exe），此时与 <see cref="ExecutableName"/> 不同。
    /// 为空时视为与 <see cref="ExecutableName"/> 相同。
    /// </summary>
    public string? ProcessName { get; init; }


    /// <summary>
    /// 截图目录。相对路径相对于游戏安装目录，也可以是完整路径
    /// （部分游戏保存到用户的图片文件夹）。
    /// </summary>
    public IReadOnlyList<string> ScreenshotPaths { get; init; } = [];


    /// <summary>
    /// 游戏本身需要的固定启动参数，与用户自定义的参数无关。
    /// 例如异环需要 /launcher。
    /// </summary>
    public string? LaunchArguments { get; init; }


    /// <summary>
    /// 支持的功能
    /// </summary>
    public GameCapability Capabilities { get; init; }


    /// <summary>
    /// 兼容层：对应的旧 GameBiz 字符串。
    /// 应用配置与数据库沿用此值作为键，不能更改，否则用户的既有数据会失效。
    /// 非米哈游游戏为 null。
    /// </summary>
    public string? LegacyGameBiz { get; init; }


    /// <summary>
    /// 应用配置与数据库使用的键。
    /// 米哈游游戏沿用旧的 GameBiz 字符串以保证既有数据不失效，
    /// 其他供应商使用 <see cref="GameKey"/> 的正规字符串（例如 kuro:wuwa:global）。
    /// 两种格式都是纯字符串，因此不需要更改数据库结构。
    /// </summary>
    public string SettingsKey => LegacyGameBiz ?? Key.ToString();


    /// <summary>
    /// 查找游戏进程时使用的名称，不带 .exe 扩展名
    /// </summary>
    public string? ProcessNameWithoutExtension
    {
        get
        {
            string? name = ProcessName ?? ExecutableName;
            return string.IsNullOrEmpty(name) ? null : Path.GetFileNameWithoutExtension(name);
        }
    }


    /// <summary>
    /// 解析截图目录为完整路径，跳过不存在的目录
    /// </summary>
    /// <param name="installPath">游戏安装目录，用于解析相对路径</param>
    public IReadOnlyList<string> ResolveScreenshotPaths(string? installPath)
    {
        var paths = new List<string>();
        foreach (string path in ScreenshotPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }
            string full;
            if (Path.IsPathFullyQualified(path))
            {
                full = path;
            }
            else if (!string.IsNullOrWhiteSpace(installPath))
            {
                full = Path.Combine(installPath, path);
            }
            else
            {
                continue;
            }
            if (Directory.Exists(full))
            {
                paths.Add(full);
            }
        }
        return paths.AsReadOnly();
    }


    /// <summary>
    /// 是否支持指定的功能，可以同时判断多个标志
    /// </summary>
    public bool HasCapability(GameCapability capability)
    {
        return capability is not GameCapability.None && (Capabilities & capability) == capability;
    }

}
