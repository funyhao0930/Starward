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
        string? arg = descriptor.LaunchArguments;
        string? custom = _settings.GetStartArgument(key)?.Trim();
        if (!string.IsNullOrWhiteSpace(custom))
        {
            arg = string.IsNullOrWhiteSpace(arg) ? custom : $"{arg} {custom}";
        }
        if (_settings.GetUsePopupWindow(key))
        {
            arg += " -popupwindow";
        }
        if (_settings.GetEnableDX12(key))
        {
            arg += " -use-d3d12";
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
            // 启动的文件不是游戏进程本身时，需要按进程名查找
            TrackByProcessName = thirdPartyTool
                              || useCommandPrompt
                              || !string.Equals(descriptor.ProcessName ?? exeName, exeName, StringComparison.OrdinalIgnoreCase),
        });
    }

}
