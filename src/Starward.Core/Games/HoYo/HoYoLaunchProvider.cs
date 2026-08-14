namespace Starward.Core.Games.HoYo;

/// <summary>
/// 米哈游游戏的启动命令构建，包含游戏专属的进程名、启动参数与启动前操作。
/// 实际创建进程由通用的启动服务负责。
/// </summary>
public class HoYoLaunchProvider : IGameLaunchProvider
{

    private readonly IGameLaunchSettings _settings;

    private readonly IHoYoLaunchHooks _hooks;


    public HoYoLaunchProvider(IGameLaunchSettings settings, IHoYoLaunchHooks hooks)
    {
        _settings = settings;
        _hooks = hooks;
    }


    public string ProviderId => HoYoGameMapping.ProviderId;


    public async ValueTask<string?> GetExecutableNameAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        string? name = HoYoGameMapping.GetExecutableName(key);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }
        // 静态表中没有的游戏，通过 HoYoPlay 接口查询
        return await _hooks.GetRemoteExecutableNameAsync(key, cancellationToken).ConfigureAwait(false);
    }


    public async ValueTask<GameLaunchCommand> CreateLaunchCommandAsync(GameKey key, GameLaunchOptions options, CancellationToken cancellationToken = default)
    {
        string exeName = await GetExecutableNameAsync(key, cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentOutOfRangeException(nameof(key), key.ToString(), "Unknown game, cannot determine the executable name.");

        string? exe = null, verb = null;
        bool thirdPartyTool = false;

        // 优先使用调用方指定的安装目录
        if (Directory.Exists(options.InstallPath))
        {
            string path = Path.Join(options.InstallPath, exeName);
            if (File.Exists(path))
            {
                exe = path;
            }
        }

        // 其次使用第三方工具
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

        // 最后使用配置中记录的安装目录
        if (string.IsNullOrWhiteSpace(exe))
        {
            exe = Path.Join(options.ConfiguredInstallPath, exeName);
            verb = "runas";
            if (!File.Exists(exe))
            {
                throw new FileNotFoundException("Game exe not found", exeName);
            }
        }

        string? arg = _settings.GetStartArgument(key)?.Trim();
        string? ticket = await _hooks.CreateAuthTicketAsync(key, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(ticket))
        {
            arg += $" login_auth_ticket={ticket}";
        }
        if (_settings.GetUsePopupWindow(key))
        {
            arg += " -popupwindow";
        }
        if (_settings.GetEnableDX12(key))
        {
            arg += " -use-d3d12";
        }

        // 游戏专属的启动前操作，例如原神 HDR
        await _hooks.ApplyPreLaunchSettingsAsync(key, cancellationToken).ConfigureAwait(false);

        bool useCommandPrompt = !thirdPartyTool && _settings.StartGameWithCommandPrompt;
        if (useCommandPrompt)
        {
            arg = $"""/c start "" /d "{Path.GetDirectoryName(exe)}" "{exe}" {arg}""";
            exe = "cmd.exe";
        }

        return new GameLaunchCommand
        {
            FileName = exe,
            Arguments = arg,
            WorkingDirectory = Path.GetDirectoryName(exe),
            Verb = verb,
            UseShellExecute = true,
            TrackByProcessName = thirdPartyTool || useCommandPrompt,
        };
    }

}
