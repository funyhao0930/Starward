using Starward.Core.Launcher.Gryphline;

namespace Starward.Core.Games.Gryphline;

/// <summary>
/// 终末地的启动。2026-10-15 的热更新起，游戏开局会检查父进程是不是官方启动器
/// （OneChannelLC.dll，日志在 <c>AppData\LocalLow\Gryphline\Endfield\sdklogs\OneChannelLC.log</c>），
/// 不是就弹「请通过官方启动器打开游戏」并退出，errorCode 10100，filePath 是父进程路径。
/// 因此和异环一样，只能请官方启动器代为启动，再按进程名追踪游戏本体。
/// <para/>
/// 用的是游戏自己检查失败时打开的 deep link <c>gryphlinklauncher-{启动器 appcode}://start?ta=endfield</c>，
/// 协议注册的命令就是 <c>"Launcher.exe" "%1"</c>。它只会把启动器切到终末地页（Games.exe 收到
/// <c>--game=endfield --reason=6</c>），不会自动开游戏，开始游戏仍要在官方启动器里按。
/// 官方启动器启动游戏时不带 Starward 的自定义参数，DX11 等开关也就不生效。
/// <para/>
/// 没有官方启动器（例如只用 Starward 安装了游戏本体）时退回直接启动，游戏会弹同一个提示。
/// </summary>
public class GryphlineGameLaunchProvider : SimpleGameLaunchProvider
{

    /// <summary>
    /// 官方启动器的入口，位于安装根目录（游戏本体在其下的 <see cref="GryphlineGameMapping.GameFolderName"/>）
    /// </summary>
    public const string LauncherExecutableName = "Launcher.exe";


    /// <summary>
    /// 让官方启动器打开终末地页的 deep link，与游戏检查失败时打开的完全相同
    /// </summary>
    public const string LauncherDeepLink = $"gryphlinklauncher-{GryphlineLauncherClient.GLOBAL_LAUNCHER_APP_CODE}://start?ta=endfield";


    public GryphlineGameLaunchProvider(IGameCatalogProvider catalog, IGameLaunchSettings settings) : base(GryphlineGameMapping.ProviderId, catalog, settings)
    {

    }



    public override ValueTask<GameLaunchCommand> CreateLaunchCommandAsync(GameKey key, GameLaunchOptions options, CancellationToken cancellationToken = default)
    {
        string? installPath = Directory.Exists(options.InstallPath) ? options.InstallPath : options.ConfiguredInstallPath;
        string? launcher = string.IsNullOrWhiteSpace(installPath) ? null : Path.Combine(installPath, LauncherExecutableName);
        if (!File.Exists(launcher))
        {
            return base.CreateLaunchCommandAsync(key, options, cancellationToken);
        }

        return ValueTask.FromResult(new GameLaunchCommand
        {
            FileName = launcher,
            Arguments = $"\"{LauncherDeepLink}\"",
            WorkingDirectory = installPath,
            UseShellExecute = true,
            // 启动的是官方启动器，游戏本体要等玩家在启动器里按开始才出现
            TrackByProcessName = true,
        });
    }

}
