using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Core.Launcher;
using Starward.Core.Launcher.Gryphline;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.RPC.GameInstall.Gryphline;

/// <summary>
/// 明日方舟：终末地的安装、更新、修复与预下载。
/// <para/>
/// 安装与修复是同一个动作：以线上最新整包的文件清单为准，逐个文件比对 CRC32，
/// 不对或缺少的从 <c>{file_path}/{相对路径}</c> 单独下载，最后清掉 VFS 目录里新清单上没有的旧文件。
/// 不照官方启动器那样下整包：整包要先放下 57 GB 的分卷再解压出 62 GB，按文件下载只需要一份空间，而且每个文件都能续传。
/// 文件清单不必解密本机的 game_files：整包末尾的 zip 中央目录是明文，带每个文件的 CRC32，
/// 见 <see cref="GryphlineLauncherClient.GetPackageEntriesAsync"/>。
/// <para/>
/// 更新与预下载走官方的补丁（见 GryphlineGameInstaller.Patch.cs）；
/// 认不出本机版本、或这个版本没有补丁时，更新退回按文件比对。
/// <see cref="GameInstallContext.InstallPath"/> 是 GRYPHLINK 的安装根目录，
/// 游戏在其下的 <see cref="GryphlineGameMapping.GameFolderName"/>，与官方的目录结构一致。
/// </summary>
internal partial class GryphlineGameInstaller : IGameInstallVendor
{

    private readonly ILogger<GryphlineGameInstaller> _logger;

    private readonly GryphlineLauncherClient _launcherClient;

    private readonly GameInstallHelper _gameInstallHelper;

    private readonly ResiliencePipeline _polly;


    public GryphlineGameInstaller(ILogger<GryphlineGameInstaller> logger, GryphlineLauncherClient launcherClient, GameInstallHelper gameInstallHelper)
    {
        _logger = logger;
        _launcherClient = launcherClient;
        _gameInstallHelper = gameInstallHelper;
        _polly = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 5,
            BackoffType = DelayBackoffType.Linear,
        }).Build();
    }


    public string ProviderId => GryphlineGameMapping.ProviderId;


    public IReadOnlyList<string> GetUninstallDirectories(GameKey key, string installPath)
    {
        return [GetGameDirectory(installPath), Path.Combine(installPath, GryphlinePatchPlanner.StagingFolderName)];
    }


    /// <summary>
    /// 游戏本体所在的目录
    /// </summary>
    public static string GetGameDirectory(string installPath) => Path.Combine(installPath, GryphlineGameMapping.GameFolderName);



    private sealed class GryphlineInstallPlan
    {
        public required string Version { get; init; }

        public required string FilePath { get; init; }

        public required IReadOnlyList<ZipEntryInfo> Files { get; init; }
    }



    public async Task ExecuteAsync(GameInstallContext context, GameKey key, CancellationToken cancellationToken)
    {
        if (key != GryphlineGameMapping.EndfieldDefault)
        {
            throw new NotSupportedException($"Unsupported game: {key}");
        }
        if (context.Operation is not (GameInstallOperation.Install or GameInstallOperation.Update or GameInstallOperation.Repair or GameInstallOperation.Predownload))
        {
            throw new NotSupportedException($"Unsupported operation: {context.Operation}");
        }
        string gameDir = GetGameDirectory(context.InstallPath);
        Directory.CreateDirectory(gameDir);
        context.State = GameInstallState.Waiting;

        if (context.Operation is GameInstallOperation.Predownload)
        {
            if (context.VendorState is not GryphlinePatchPlan predownload)
            {
                predownload = await PreparePredownloadAsync(context, gameDir, cancellationToken);
                context.VendorState = predownload;
            }
            await DownloadPatchPartsAsync(context, predownload, cancellationToken);
            await WritePredownloadMarkerAsync(predownload, cancellationToken);
            return;
        }
        if (context.Operation is GameInstallOperation.Update && context.VendorState is not GryphlineInstallPlan)
        {
            // 有补丁就打补丁；问不到补丁、或认不出本机版本时，退回按文件比对
            GryphlinePatchPlan? patch = context.VendorState as GryphlinePatchPlan ?? await TryPrepareUpdateAsync(context, gameDir, cancellationToken);
            if (patch is not null)
            {
                context.VendorState = patch;
                await UpdateByPatchAsync(context, patch, gameDir, cancellationToken);
                return;
            }
        }
        await SyncFilesAsync(context, gameDir, cancellationToken);
        if (context.Operation is GameInstallOperation.Update)
        {
            // 暂存的补丁（含预下载）都是旧的了
            DeleteStagingRoot(context.InstallPath);
        }
    }



    /// <summary>
    /// 安装、修复与没有补丁时的更新：按整包的文件清单逐个比对、下载
    /// </summary>
    private async Task SyncFilesAsync(GameInstallContext context, string gameDir, CancellationToken cancellationToken)
    {
        if (context.VendorState is not GryphlineInstallPlan plan)
        {
            plan = await PrepareAsync(context, cancellationToken);
            context.VendorState = plan;
        }

        foreach (string file in Directory.EnumerateFiles(gameDir, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        var manifestNames = new HashSet<string>(GryphlineDownloadPlanner.ManifestFiles, StringComparer.OrdinalIgnoreCase);
        List<ZipEntryInfo> files = plan.Files.Where(x => !manifestNames.Contains(x.Name)).ToList();
        List<ZipEntryInfo> manifestFiles = plan.Files.Where(x => manifestNames.Contains(x.Name)).ToList();

        context.Progress_DownloadTotalBytes = plan.Files.Sum(x => x.Size);
        context.Progress_DownloadFinishBytes = 0;
        context.State = GameInstallState.Downloading;
        _logger.LogInformation("Endfield: sync {count} files ({size} bytes) to {dir}.", plan.Files.Count, context.Progress_DownloadTotalBytes, gameDir);
        await Parallel.ForEachAsync(files, cancellationToken, async (file, token) =>
        {
            await DownloadFileAsync(context, plan, gameDir, file, token);
        });
        // 全部就位之后才写清单：官方启动器与 Starward 都靠 game_files 判断是不是最新版
        foreach (ZipEntryInfo file in manifestFiles)
        {
            await DownloadFileAsync(context, plan, gameDir, file, cancellationToken);
        }

        if (context.Operation is GameInstallOperation.Update or GameInstallOperation.Repair)
        {
            DeleteStaleVfsFiles(plan, gameDir);
        }
        _logger.LogInformation("Endfield: {operation} finished, version {version}.", context.Operation, plan.Version);
    }



    private async Task<GryphlineInstallPlan> PrepareAsync(GameInstallContext context, CancellationToken cancellationToken)
    {
        // 版本号留空才会带分卷列表，文件清单要从最后一卷读
        GryphlineLatestGame latest = await _launcherClient.GetLatestGameAsync(GryphlineLauncherClient.ENDFIELD_APP_CODE, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Endfield game package is not available.");
        GryphlineGamePackage package = latest.Package
            ?? throw new InvalidOperationException("Endfield game package is not available.");
        if (string.IsNullOrWhiteSpace(package.FilePath))
        {
            throw new InvalidOperationException("Endfield game package has no file path.");
        }
        string version = latest.Version ?? throw new InvalidOperationException("Endfield game package has no version.");
        // 读中央目录只有两小段，连接卡住会抛 TimeoutException，与下载文件一样重试
        IReadOnlyList<ZipEntryInfo> entries = await _polly.ExecuteAsync(async token => await _launcherClient.GetPackageEntriesAsync(package, token), cancellationToken);
        if (entries.FirstOrDefault(x => x.IsEncrypted) is ZipEntryInfo encrypted)
        {
            throw new NotSupportedException($"Encrypted entry in the Endfield package: {encrypted.Name}");
        }
        IReadOnlyList<ZipEntryInfo> files = GryphlineDownloadPlanner.GetFiles(entries);
        context.LatestGameVersion = version;
        context.DownloadMode = GameInstallDownloadMode.SingleFile;
        _logger.LogInformation("""
            Prepare Endfield task finished:
            Operation: {operation}
            Version: {version}
            Files: {count} ({size} bytes)
            FilePath: {filePath}
            """, context.Operation, version, files.Count, files.Sum(x => x.Size), package.FilePath);
        return new GryphlineInstallPlan
        {
            Version = version,
            FilePath = package.FilePath,
            Files = files,
        };
    }



    private async Task DownloadFileAsync(GameInstallContext context, GryphlineInstallPlan plan, string gameDir, ZipEntryInfo file, CancellationToken cancellationToken)
    {
        string path = ToFullPath(gameDir, file.Name);
        string url = GryphlineDownloadPlanner.GetFileUrl(plan.FilePath, file.Name);
        await _polly.ExecuteAsync(async token => await _gameInstallHelper.DownloadToFileAsync(context, path, url, file.Size, FileChecksum.Crc32(file.Crc32), token), cancellationToken);
    }



    /// <summary>
    /// 更新后旧版本的 VFS 资源会改名留下，官方靠补丁里的 delete_files.txt 删；
    /// 按文件更新没有那份清单，改为删掉 VFS 目录里新清单上没有的文件。
    /// 其他目录一律不碰，例如玩家自己换过的 DLSS 文件。
    /// </summary>
    private void DeleteStaleVfsFiles(GryphlineInstallPlan plan, string gameDir)
    {
        string vfs = ToFullPath(gameDir, GryphlineDownloadPlanner.VfsDirectory);
        if (!Directory.Exists(vfs))
        {
            return;
        }
        var local = Directory.EnumerateFiles(vfs, "*", SearchOption.AllDirectories)
                             .Select(x => Path.GetRelativePath(gameDir, x).Replace('\\', '/'))
                             .ToList();
        IReadOnlyList<string> stale = GryphlineDownloadPlanner.GetStaleVfsFiles(local, plan.Files);
        long bytes = 0;
        foreach (string relative in stale)
        {
            string path = ToFullPath(gameDir, relative);
            bytes += new FileInfo(path).Length;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        _logger.LogInformation("Endfield: deleted {count} stale VFS files ({bytes} bytes).", stale.Count, bytes);
    }



    /// <summary>
    /// 清单里的路径是相对的正斜杠路径，拼完要确认没有跑出游戏目录
    /// </summary>
    private static string ToFullPath(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('\\', '/').TrimStart('/')));
        string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) && !string.Equals(full + Path.DirectorySeparatorChar, rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Path escapes the game directory: {relative}");
        }
        return full;
    }

}
