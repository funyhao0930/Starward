using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.Games;
using Starward.Core.HoYoPlay;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace Starward.RPC.GameInstall;

internal class GameUninstallService
{

    private readonly ILogger<GameUninstallService> _logger;


    private readonly HoYoPlayClient _hoYoPlayClient;

    private readonly IEnumerable<IGameInstallVendor> _vendors;


    public GameUninstallService(ILogger<GameUninstallService> logger, HoYoPlayClient hoYoPlayClient, IEnumerable<IGameInstallVendor> vendors)
    {
        _logger = logger;
        _hoYoPlayClient = hoYoPlayClient;
        _vendors = vendors;
    }





    public async Task UninstallGameAsync(UninstallGameRequest request, CancellationToken cancellationToken = default)
    {
        string installPath = request.InstallPath;
        if (!Directory.Exists(installPath))
        {
            _logger.LogWarning("Game folder does not exist: {installPath}", installPath);
        }
        if (Path.GetPathRoot(installPath) == installPath)
        {
            _logger.LogError("Game folder is the root of drive.");
            throw new InvalidOperationException("Game folder is the root of drive.");
        }
        if (GameKeyResolver.Resolve(request.GameBiz) is GameKey key && !key.IsProvider(GameProviderIds.HoYo)
            && _vendors.FirstOrDefault(x => key.IsProvider(x.ProviderId)) is IGameInstallVendor vendor)
        {
            UninstallVendorGame(request, key, vendor);
            return;
        }
        _logger.LogInformation("Start to uninstall game ({gameBiz}): {installPath}", request.GameBiz, installPath);
        GameId gameId = new GameId { GameBiz = request.GameBiz, Id = request.GameId };
        GameConfig? gameConfig = null;
        try
        {
            gameConfig = await _hoYoPlayClient.GetGameConfigAsync(LauncherId.FromGameId(gameId)!, "en-us", gameId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get game config.");
        }
        string[] files = Directory.GetFiles(installPath, "*", SearchOption.AllDirectories);
        foreach (string file in files)
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        BackupScreenshot(request, gameConfig);
        _logger.LogInformation("Deleting folder {installPath} ({count} files).", installPath, files.Length);
        Directory.Delete(installPath, true);
        ClearCacheDir(request, gameConfig);
        _logger.LogInformation("Finished uninstall game ({gameBiz}): {installPath}", request.GameBiz, installPath);
    }




    /// <summary>
    /// 只删游戏本体与 Starward 自己的暂存目录，官方启动器留着；
    /// 删完如果安装根目录空了，才把它也删掉。
    /// </summary>
    private void UninstallVendorGame(UninstallGameRequest request, GameKey key, IGameInstallVendor vendor)
    {
        string installPath = request.InstallPath;
        _logger.LogInformation("Start to uninstall game ({key}): {installPath}", key, installPath);
        IReadOnlyList<string> dirs = vendor.GetUninstallDirectories(key, installPath);
        string installFull = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string dir in dirs)
        {
            string full = Path.GetFullPath(dir);
            if (!full.StartsWith(installFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Uninstall directory is outside the install path: {dir}");
            }
        }
        string? gameDir = dirs.FirstOrDefault(Directory.Exists);
        if (gameDir is not null)
        {
            // 截图多半在游戏目录里（鸣潮是 Client\Saved\ScreenShot），按名字找
            BackupScreenshot(new UninstallGameRequest(request) { InstallPath = gameDir }, null);
        }
        foreach (string dir in dirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }
            string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            _logger.LogInformation("Deleting folder {dir} ({count} files).", dir, files.Length);
            Directory.Delete(dir, true);
            // 终末地的游戏在 games\EndField Game，删完之后空着的 games 也一并清掉
            string? parent = Path.GetDirectoryName(Path.GetFullPath(dir));
            while (parent is not null
                   && parent.Length + 1 > installFull.Length
                   && Directory.Exists(parent)
                   && !Directory.EnumerateFileSystemEntries(parent).Any())
            {
                Directory.Delete(parent);
                parent = Path.GetDirectoryName(parent);
            }
        }
        if (Directory.Exists(installPath) && !Directory.EnumerateFileSystemEntries(installPath).Any())
        {
            Directory.Delete(installPath);
        }
        _logger.LogInformation("Finished uninstall game ({key}): {installPath}", key, installPath);
    }



    private void BackupScreenshot(UninstallGameRequest request, GameConfig? gameConfig)
    {
        string userDataFolder = request.UserDataFolder;
        string sourceScreenshotFolder;
        if (gameConfig is null)
        {
            string[] dirs = Directory.GetDirectories(request.InstallPath, "Screenshot*", SearchOption.AllDirectories);
            sourceScreenshotFolder = dirs.FirstOrDefault() ?? "";
        }
        else
        {
            sourceScreenshotFolder = Path.Join(request.InstallPath, gameConfig.GameScreenshotDir);
        }

        string backupBaseFolder;
        string backupScreenshotFolder;
        if (Directory.Exists(request.ScreenshotFolder))
        {
            backupBaseFolder = request.ScreenshotFolder;
        }
        else
        {
            backupBaseFolder = Path.Join(userDataFolder, "Screenshots");
        }
        if (!string.IsNullOrWhiteSpace(request.GameExeName))
        {
            backupScreenshotFolder = Path.Join(backupBaseFolder, Path.GetFileNameWithoutExtension(request.GameExeName));
        }
        else if (!string.IsNullOrWhiteSpace(gameConfig?.ExeFileName))
        {
            backupScreenshotFolder = Path.Join(backupBaseFolder, Path.GetFileNameWithoutExtension(gameConfig.ExeFileName));
        }
        else
        {
            backupScreenshotFolder = Path.Join(backupBaseFolder, ((GameBiz)request.GameBiz).Game);
        }
        if (Directory.Exists(userDataFolder) && Directory.Exists(sourceScreenshotFolder))
        {
            bool canHardLink = CanHardLink(sourceScreenshotFolder, backupScreenshotFolder);
            Directory.CreateDirectory(backupScreenshotFolder);
            string[] files = Directory.GetFiles(sourceScreenshotFolder);
            int count = 0;
            foreach (string file in files)
            {
                string target = Path.Join(backupScreenshotFolder, Path.GetFileName(file));
                if (!File.Exists(target))
                {
                    bool result = false;
                    if (canHardLink)
                    {
                        result = Kernel32.CreateHardLink(target, file);
                    }
                    if (!result)
                    {
                        File.Copy(file, target, true);
                    }
                    count++;
                }
            }
            _logger.LogInformation("Backed up {count} screenshots.", count);
        }
    }



    private void ClearCacheDir(UninstallGameRequest request, GameConfig? gameConfig)
    {
        if (gameConfig is null)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(gameConfig.GameLogGenDir))
        {
            string path = Environment.ExpandEnvironmentVariables(gameConfig.GameLogGenDir);
            if (Path.IsPathFullyQualified(path))
            {
                if (path.Contains("miHoYo", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("Cognosphere", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("HoYoverse", StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                        _logger.LogInformation("Deleted folder {path}", path);
                    }
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(gameConfig.GameCrashFileGenDir))
        {
            string path = Environment.ExpandEnvironmentVariables(gameConfig.GameCrashFileGenDir);
            if (Path.IsPathFullyQualified(path))
            {
                // 防止原神把 %UserProfile%/AppData/Local/Temp 删了
                if (path.Contains("miHoYo", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("Cognosphere", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("HoYoverse", StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                        _logger.LogInformation("Deleted folder {path}", path);
                    }
                }
            }
        }
    }




    private static bool CanHardLink(string source, string dest)
    {
        return Path.GetPathRoot(source) == Path.GetPathRoot(dest) && DriveHelper.GetDriveFormat(dest) is "NTFS";
    }




}
