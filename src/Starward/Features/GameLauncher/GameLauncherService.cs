using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Core.HoYoPlay;
using Starward.Features.HoYoPlay;
using Starward.Features.PlayTime;
using Starward.Helpers;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 通用的游戏启动服务。只负责与游戏无关的部分：
/// 检查游戏是否正在运行、执行 <see cref="ProcessStartInfo"/>、记录游玩时间、管理员权限、错误处理。
/// <para/>
/// 游戏专属的进程名、启动参数、启动前操作由 <see cref="IGameLaunchProvider"/> 负责。
/// </summary>
internal partial class GameLauncherService
{


    private readonly ILogger<GameLauncherService> _logger;


    private readonly HoYoPlayService _hoYoPlayService;

    private readonly PlayTimeRecordService _playTimeRecorderService;

    private readonly IGameProviderRegistry _providerRegistry;


    public GameLauncherService(ILogger<GameLauncherService> logger, HoYoPlayService hoYoPlayService, PlayTimeRecordService playTimeRecorderService, IGameProviderRegistry providerRegistry)
    {
        _logger = logger;
        _hoYoPlayService = hoYoPlayService;
        _playTimeRecorderService = playTimeRecorderService;
        _providerRegistry = providerRegistry;
    }





    /// <summary>
    /// 游戏安装目录，为空时未找到
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    public static string? GetGameInstallPath(GameKey key)
    {
        return GetGameInstallPath(SettingsKey(key));
    }


    /// <summary>
    /// 游戏安装目录，为空时未找到
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    public static string? GetGameInstallPath(GameBiz gameBiz)
    {
        var path = AppConfig.GetGameInstallPath(gameBiz);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        path = GetFullPathIfRelativePath(path);
        if (Directory.Exists(path))
        {
            return Path.GetFullPath(path);
        }
        else if (AppConfig.GetGameInstallPathRemovable(gameBiz))
        {
            return path;
        }
        else
        {
            ChangeGameInstallPath(gameBiz, null);
            return null;
        }
    }



    /// <summary>
    /// 游戏安装目录，为空时未找到
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="storageRemoved">可移动存储设备已移除</param>
    /// <returns></returns>
    public static string? GetGameInstallPath(GameKey key, out bool storageRemoved)
    {
        GameBiz gameBiz = SettingsKey(key);
        storageRemoved = false;
        var path = AppConfig.GetGameInstallPath(gameBiz);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        path = GetFullPathIfRelativePath(path);
        if (Directory.Exists(path))
        {
            return path;
        }
        else if (AppConfig.GetGameInstallPathRemovable(gameBiz))
        {
            storageRemoved = true;
            return path;
        }
        else
        {
            ChangeGameInstallPath(gameBiz, null);
            return null;
        }
    }



    /// <summary>
    /// 本地游戏版本
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="installPath"></param>
    /// <returns></returns>
    public async Task<Version?> GetLocalGameVersionAsync(GameKey key, string? installPath = null)
    {
        return await GetLocalGameVersionAsync(SettingsKey(key), installPath);
    }



    /// <summary>
    /// 本地游戏版本
    /// </summary>
    /// <param name="gameBiz"></param>
    /// <param name="installPath"></param>
    /// <returns></returns>
    public async Task<Version?> GetLocalGameVersionAsync(GameBiz gameBiz, string? installPath = null)
    {
        installPath ??= GetGameInstallPath(gameBiz);
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }
        // 每家记录版本的方式都不同，先问对应的 Provider
        if (GameKeyResolver.Resolve(gameBiz.Value) is GameKey key
            && _providerRegistry.GetDiscoveryProvider(key.ProviderId) is IGameDiscoveryProvider discovery
            && await discovery.GetLocalVersionAsync(key, installPath) is Version providerVersion)
        {
            return providerVersion;
        }
        // 米哈游游戏记录在安装目录的 config.ini 中
        var config = Path.Join(installPath, "config.ini");
        if (File.Exists(config))
        {
            var str = await File.ReadAllTextAsync(config);
            var matches = GameVersionRegex().Matches(str);
            Version? version = null;
            if (matches.Count > 0)
            {
                _ = Version.TryParse(matches[^1].Groups[1].Value, out version);
            }
            return version;
        }
        else
        {
            _logger.LogWarning("config.ini not found: {path}", config);
            return null;
        }
    }


    [GeneratedRegex(@"game_version=(.+)")]
    private static partial Regex GameVersionRegex();



    /// <summary>
    /// 最新游戏版本
    /// </summary>
    /// <param name="gameBiz"></param>
    /// <returns></returns>
    public async Task<(Version? Latest, Version? Predownload)> GetLatestGameVersionAsync(GameId gameId)
    {
        GameConfig? config = await _hoYoPlayService.GetGameConfigAsync(gameId);
        if (config is null)
        {
            throw new ArgumentOutOfRangeException($"Game config is null ({gameId.Id}, {gameId.GameBiz}).");
        }
        if (config.DefaultDownloadMode is DownloadMode.DOWNLOAD_MODE_CHUNK or DownloadMode.DOWNLOAD_MODE_LDIFF)
        {
            GameBranch? gameBranch = await _hoYoPlayService.GetGameBranchAsync(gameId);
            if (gameBranch is null)
            {
                throw new ArgumentOutOfRangeException($"Game branch is null ({gameId.Id}, {gameId.GameBiz}).");
            }
            _ = Version.TryParse(gameBranch.Main.Tag, out Version? latestVersion);
            _ = Version.TryParse(gameBranch.PreDownload?.Tag, out Version? predownloadVersion);
            return (latestVersion, predownloadVersion);
        }
        else
        {
            GamePackage package = await _hoYoPlayService.GetGamePackageAsync(gameId);
            _ = Version.TryParse(package.Main.Major?.Version, out Version? latestVersion);
            _ = Version.TryParse(package.PreDownload.Major?.Version, out Version? predownloadVersion);
            return (latestVersion, predownloadVersion);
        }
    }




    /// <summary>
    /// 通用游戏标识
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    /// <summary>
    /// 应用配置与数据库使用的键
    /// </summary>
    private static GameBiz SettingsKey(GameKey key) => new(GameKeyResolver.ToSettingsKey(key));



    /// <summary>
    /// 启动时执行的文件名，带 .exe 扩展名。由对应的 <see cref="IGameLaunchProvider"/> 提供。
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    public async Task<string> GetGameExeNameAsync(GameKey key)
    {
        string? name = await _providerRegistry.GetRequiredLaunchProvider(key).GetExecutableNameAsync(key);
        return name ?? throw new ArgumentOutOfRangeException(nameof(key), key.ToString(), "Unknown game.");
    }



    /// <summary>
    /// 查找游戏进程时使用的名称，不带 .exe 扩展名。
    /// 部分游戏启动的是一层外壳，进程名与启动的文件名不同。
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    public async Task<string> GetGameProcessNameAsync(GameKey key)
    {
        if (_providerRegistry.GetGame(key)?.ProcessNameWithoutExtension is string processName)
        {
            return processName;
        }
        return (await GetGameExeNameAsync(key)).Replace(".exe", "");
    }



    /// <summary>
    /// 游戏进程文件是否存在
    /// </summary>
    /// <param name="biz"></param>
    /// <param name="installPath"></param>
    /// <returns></returns>
    public async Task<bool> IsGameExeExistsAsync(GameKey key, string? installPath = null)
    {
        installPath ??= GetGameInstallPath(key);
        if (!string.IsNullOrWhiteSpace(installPath))
        {
            var exe = Path.Join(installPath, await GetGameExeNameAsync(key));
            return File.Exists(exe);
        }
        return false;
    }



    /// <summary>
    /// 获取游戏进程
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    public async Task<Process?> GetGameProcessAsync(GameKey key)
    {
        int currentSessionId = Process.GetCurrentProcess().SessionId;
        string name = await GetGameProcessNameAsync(key);
        return Process.GetProcessesByName(name).Where(x => x.SessionId == currentSessionId && !IsProcessPending(x)).FirstOrDefault();
    }



    /// <summary>
    /// 检测进程挂起
    /// </summary>
    /// <param name="process"></param>
    /// <returns></returns>
    public static bool IsProcessPending(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return false;
            }
            foreach (ProcessThread thread in process.Threads)
            {
                if (thread.ThreadState is not ThreadState.Wait)
                {
                    return false;
                }
                else if (thread.WaitReason is not ThreadWaitReason.Suspended)
                {
                    return false;
                }
            }
            return true;
        }
        catch { }
        return true;
    }



    /// <summary>
    /// 启动游戏。游戏专属的部分由 <see cref="IGameLaunchProvider"/> 构建成 <see cref="GameLaunchCommand"/>，
    /// 本方法只负责执行、记录游玩时间与错误处理。
    /// </summary>
    /// <returns></returns>
    public async Task<Process?> StartGameAsync(GameKey key, string? installPath = null)
    {
        const int ERROR_CANCELLED = 0x000004C7;
        try
        {
            if (await GetGameProcessAsync(key) is Process existingProcess)
            {
                throw new Exception($"Game is running: {existingProcess.ProcessName}.exe ({existingProcess.Id}).");
            }

            var options = new GameLaunchOptions
            {
                InstallPath = installPath,
                ConfiguredInstallPath = GetGameInstallPath(key),
            };
            GameLaunchCommand command;
            try
            {
                command = await _providerRegistry.GetRequiredLaunchProvider(key).CreateLaunchCommandAsync(key, options);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning("Game exe not found: {name}", ex.FileName);
                throw;
            }

            _logger.LogInformation("Start game ({key})\r\npath: {exe}\r\narg: {arg}", key, command.FileName, command.Arguments);
            var info = new ProcessStartInfo
            {
                FileName = command.FileName,
                Arguments = command.Arguments,
                UseShellExecute = command.UseShellExecute,
                Verb = command.Verb,
                WorkingDirectory = command.WorkingDirectory,
            };
            Process? process = Process.Start(info);
            if (process != null)
            {
                if (command.TrackByProcessName)
                {
                    // 创建出来的进程是第三方工具或 cmd.exe，需要按进程名查找真正的游戏进程
                    return await _playTimeRecorderService.StartProcessToLogAsync(key);
                }
                else
                {
                    await _playTimeRecorderService.StartProcessToLogAsync(key, process.Id);
                    return process;
                }
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            // Operation canceled
            _logger.LogInformation("Start game operation canceled.");
        }
        return null;
    }




    /// <summary>
    /// 修改游戏安装目录
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public static string? ChangeGameInstallPath(GameKey key, string? path)
    {
        return ChangeGameInstallPath(SettingsKey(key), path);
    }


    /// <summary>
    /// 修改游戏安装目录
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public static string? ChangeGameInstallPath(GameBiz gameBiz, string? path)
    {
        if (Directory.Exists(path))
        {
            path = Path.GetFullPath(path);
            string relativePath = GetRelativePathIfInRemovableStorage(path, out bool removable);
            AppConfig.SetGameInstallPath(gameBiz, relativePath);
            AppConfig.SetGameInstallPathRemovable(gameBiz, removable);
        }
        else
        {
            path = null;
            AppConfig.SetGameInstallPath(gameBiz, null);
            AppConfig.SetGameInstallPathRemovable(gameBiz, false);
        }
        return path;
    }



    /// <summary>
    /// 如果安装在可移动存储设备中，获取相对路径
    /// </summary>
    /// <param name="path"></param>
    /// <param name="removableStorage"></param>
    /// <returns></returns>
    public static string GetRelativePathIfInRemovableStorage(string path, out bool removableStorage)
    {
        removableStorage = DriveHelper.IsDeviceRemovableOrOnUSB(path);
        if (removableStorage && Path.GetPathRoot(AppConfig.StarwardExecutePath) == Path.GetPathRoot(path))
        {
            path = Path.GetRelativePath(Path.GetDirectoryName(AppConfig.ConfigPath)!, path);
        }
        return path;
    }



    /// <summary>
    /// 如果安装在可移动存储设备中，获取完整路径
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static string GetFullPathIfRelativePath(string path)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }
        else
        {
            return Path.GetFullPath(path, Path.GetDirectoryName(AppConfig.ConfigPath)!);
        }
    }




    /// <summary>
    /// 获取第三方工具路径
    /// </summary>
    /// <param name="gameId"></param>
    /// <returns></returns>
    public static string? GetThirdPartyToolPath(GameKey key)
    {
        string? path = AppConfig.GetThirdPartyToolPath(SettingsKey(key));
        if (!string.IsNullOrWhiteSpace(path))
        {
            path = GetFullPathIfRelativePath(path);
        }
        if (File.Exists(path))
        {
            return path;
        }
        else
        {
            AppConfig.SetThirdPartyToolPath(SettingsKey(key), null);
            return null;
        }
    }


    /// <summary>
    /// 设置第三方工具路径
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public static string? SetThirdPartyToolPath(GameKey key, string? path)
    {
        if (File.Exists(path))
        {
            path = Path.GetFullPath(path);
            string relativePath = GetRelativePathIfInRemovableStorage(path, out bool removable);
            AppConfig.SetThirdPartyToolPath(SettingsKey(key), relativePath);
        }
        else
        {
            path = null;
            AppConfig.SetThirdPartyToolPath(SettingsKey(key), null);
        }
        return path;
    }




}
