namespace Starward.Core.Games;

/// <summary>
/// 启动命令构建的通用实现，适用于「安装目录下执行一个固定文件」的游戏。
/// 支持第三方工具、cmd.exe 中转与用户自定义参数，与米哈游游戏的行为一致。
/// </summary>
public class SimpleGameLaunchProvider : IGameLaunchProvider
{

    private readonly IGameCatalogProvider _catalog;

    private readonly IGameLaunchSettings _settings;


    public SimpleGameLaunchProvider(string providerId, IGameCatalogProvider catalog, IGameLaunchSettings settings)
    {
        ProviderId = providerId;
        _catalog = catalog;
        _settings = settings;
    }


    public string ProviderId { get; }


    /// <summary>
    /// 用户设置，给要看设置决定参数的子类用
    /// </summary>
    protected IGameLaunchSettings Settings => _settings;


    public ValueTask<string?> GetExecutableNameAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_catalog.GetGame(key)?.ExecutableName);
    }


    public ValueTask<GameLaunchCommand> CreateLaunchCommandAsync(GameKey key, GameLaunchOptions options, CancellationToken cancellationToken = default)
    {
        GameDescriptor descriptor = _catalog.GetGame(key)
            ?? throw new ArgumentOutOfRangeException(nameof(key), key.ToString(), "Unknown game.");
        string exeName = descriptor.ExecutableName
            ?? throw new ArgumentOutOfRangeException(nameof(key), key.ToString(), "Unknown game, cannot determine the executable name.");

        string? exe = null, verb = null;
        bool thirdPartyTool = false;

        if (Directory.Exists(options.InstallPath))
        {
            string path = Path.Combine(options.InstallPath, exeName);
            if (File.Exists(path))
            {
                exe = path;
            }
        }

        if (string.IsNullOrWhiteSpace(exe) && _settings.GetEnableThirdPartyTool(key))
        {
            exe = _settings.GetThirdPartyToolPath(key);
            if (File.Exists(exe))
            {
                thirdPartyTool = true;
                verb = Path.GetExtension(exe) is ".exe" or ".bat" ? "runas" : "";
            }
            else
            {
                exe = null;
                _settings.ClearThirdPartyToolPath(key);
            }
        }

        if (string.IsNullOrWhiteSpace(exe))
        {
            exe = Path.Combine(options.ConfiguredInstallPath ?? "", exeName);
            verb = "runas";
            if (!File.Exists(exe))
            {
                throw new FileNotFoundException("Game exe not found", exeName);
            }
        }

        // 游戏本身需要的固定参数，加上用户自定义的参数
        string? installPath = Directory.Exists(options.InstallPath) ? options.InstallPath : options.ConfiguredInstallPath;
        string? arg = BuildArguments(descriptor, key, installPath, _settings.GetStartArgument(key)?.Trim());
        if (_settings.GetUsePopupWindow(key))
        {
            arg += " -popupwindow";
        }
        if (_settings.GetEnableDX12(key))
        {
            arg += " -use-d3d12";
        }
        // DX11 与 DLSS 的开关写法各游戏不同，参数由游戏描述给出；
        // 没给就说明这款游戏没有这个选项，界面上也不会显示。
        if (_settings.GetEnableDX11(key) && !string.IsNullOrWhiteSpace(descriptor.DX11LaunchArgument))
        {
            arg += $" {descriptor.DX11LaunchArgument}";
        }
        if (_settings.GetDisableDlss(key) && !string.IsNullOrWhiteSpace(descriptor.DisableDlssLaunchArgument))
        {
            arg += $" {descriptor.DisableDlssLaunchArgument}";
        }

        bool useCommandPrompt = !thirdPartyTool && _settings.StartGameWithCommandPrompt;
        if (useCommandPrompt)
        {
            arg = $"""/c start "" /d "{Path.GetDirectoryName(exe)}" "{exe}" {arg}""";
            exe = "cmd.exe";
        }

        return ValueTask.FromResult(new GameLaunchCommand
        {
            FileName = exe,
            Arguments = arg,
            WorkingDirectory = Path.GetDirectoryName(exe),
            Verb = verb,
            UseShellExecute = true,
            // 启动的文件不是游戏进程本身时，需要按进程名查找。
            // ExecutableName 可能带有相对目录，只比较文件名。
            TrackByProcessName = thirdPartyTool
                              || useCommandPrompt
                              || !string.Equals(descriptor.ProcessName ?? exeName,
                                                Path.GetFileName(exeName),
                                                StringComparison.OrdinalIgnoreCase),
        });
    }


    /// <summary>
    /// 游戏本身需要的固定参数加上用户自定义的参数，游戏的在前。
    /// 固定参数要看本机装了什么才能决定的游戏（鸣潮的资源分级）覆写这里。
    /// </summary>
    /// <param name="installPath">游戏安装目录，可能为 null 或不存在（例如改用第三方工具启动）</param>
    /// <param name="startArgument">用户自定义的参数，已去掉首尾空白</param>
    protected virtual string? BuildArguments(GameDescriptor descriptor, GameKey key, string? installPath, string? startArgument)
    {
        return JoinArguments(descriptor.LaunchArguments, startArgument);
    }


    /// <summary>
    /// 用空格连接参数，跳过空的；全部为空时返回 null
    /// </summary>
    protected static string? JoinArguments(params string?[] arguments)
    {
        string joined = string.Join(' ', arguments.Where(x => !string.IsNullOrWhiteSpace(x)));
        return joined.Length > 0 ? joined : null;
    }

}
