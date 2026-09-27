using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Starward.Core.Games;
using Starward.Core.Games.Kuro;
using Starward.Core.Launcher.Kuro;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.RPC.GameInstall.Kuro;

/// <summary>
/// 鸣潮的安装、更新、修复与预下载。
/// <para/>
/// 官方的做法（取自官方启动器 launcher_main.dll 的字符串与线上的配置）：
/// <list type="bullet">
/// <item>安装：完整清单（config.indexFile）里的每个文件逐个下载，没有压缩包。</item>
/// <item>修复：同一份清单逐个比对 MD5，坏的重下；再按 experiment.repair 的目录清单删掉多余的 pak。</item>
/// <item>更新：按本机版本在 patchConfig 里找补丁清单。近的版本是 krpdiff 目录差分加上热更新文件，
/// 远的版本只是「哪些文件变了」的清单；找不到就只能按完整清单比对。</item>
/// <item>预下载：与更新相同，只是先把要下载的东西放到暂存目录，正式更新时再打补丁。</item>
/// </list>
/// <see cref="GameInstallContext.InstallPath"/> 是官方启动器的安装根目录，
/// 游戏本体在其下的 <see cref="KuroGameMapping.GameFolderName"/>，与官方的目录结构一致。
/// </summary>
internal class KuroGameInstaller : IGameInstallVendor
{

    private const string DiffFolderName = "diff";

    private const string FilesFolderName = "files";

    private const string WorkFolderName = "work";

    /// <summary>
    /// 已经打完的差分包，暂停或中断后继续时跳过
    /// </summary>
    private const string JournalFileName = "applied.json";


    private readonly ILogger<KuroGameInstaller> _logger;

    private readonly KuroLauncherClient _launcherClient;

    private readonly GameInstallHelper _gameInstallHelper;

    private readonly KuroDirDiffPatcher _patcher;

    private readonly ResiliencePipeline _polly;


    public KuroGameInstaller(ILogger<KuroGameInstaller> logger, KuroLauncherClient launcherClient, GameInstallHelper gameInstallHelper, KuroDirDiffPatcher patcher)
    {
        _logger = logger;
        _launcherClient = launcherClient;
        _gameInstallHelper = gameInstallHelper;
        _patcher = patcher;
        _polly = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Linear,
        }).Build();
    }


    public string ProviderId => KuroGameMapping.ProviderId;



    public async Task ExecuteAsync(GameInstallContext context, GameKey key, CancellationToken cancellationToken)
    {
        if (key != KuroGameMapping.WutheringWavesGlobal)
        {
            throw new NotSupportedException($"Unsupported game: {key}");
        }
        string gameDir = GetGameDirectory(context.InstallPath);
        Directory.CreateDirectory(gameDir);

        context.State = GameInstallState.Waiting;
        if (context.VendorState is not KuroInstallPlan plan)
        {
            plan = await PrepareAsync(context, gameDir, cancellationToken);
            context.VendorState = plan;
        }

        foreach (string file in Directory.EnumerateFiles(gameDir, "*", SearchOption.AllDirectories))
        {
            // 与米哈游那边一样，只读文件会让覆盖失败
            File.SetAttributes(file, FileAttributes.Normal);
        }

        switch (context.Operation)
        {
            case GameInstallOperation.Install:
                await DownloadFullAsync(context, plan, gameDir, cancellationToken);
                WriteLocalVersion(gameDir, plan.TargetVersion);
                break;
            case GameInstallOperation.Repair:
                await DownloadFullAsync(context, plan, gameDir, cancellationToken);
                DeleteRedundantFiles(plan, gameDir);
                WriteLocalVersion(gameDir, plan.TargetVersion);
                break;
            case GameInstallOperation.Update:
                if (plan.Patch is not null)
                {
                    await UpdateByPatchAsync(context, plan, gameDir, cancellationToken);
                }
                else
                {
                    await UpdateByFullIndexAsync(context, plan, gameDir, cancellationToken);
                }
                WriteLocalVersion(gameDir, plan.TargetVersion);
                DeleteStaging(plan);
                break;
            case GameInstallOperation.Predownload:
                if (plan.Patch is null)
                {
                    // 界面只在有补丁时才开放预下载，走到这里说明补丁在两次查询之间没了
                    throw new NotSupportedException("No patch is available for predownloading from the local version.");
                }
                await DownloadPatchAsync(context, plan, cancellationToken);
                await WritePredownloadMarkerAsync(plan, cancellationToken);
                break;
            default:
                throw new NotSupportedException($"Unsupported operation: {context.Operation}");
        }
    }



    public IReadOnlyList<string> GetUninstallDirectories(GameKey key, string installPath)
    {
        return [GetGameDirectory(installPath), Path.Combine(installPath, KuroDownloadPlanner.StagingFolderName)];
    }


    /// <summary>
    /// 游戏本体所在的目录
    /// </summary>
    public static string GetGameDirectory(string installPath) => Path.Combine(installPath, KuroGameMapping.GameFolderName);



    #region Prepare



    private sealed class KuroInstallPlan
    {
        public required string TargetVersion { get; init; }

        public string? LocalVersion { get; init; }

        public required IReadOnlyList<string> CdnBases { get; init; }

        public required KuroLauncherGameConfig Config { get; init; }

        public required KuroResourceIndex FullIndex { get; init; }

        public KuroPatchPlan? Patch { get; init; }

        public required IReadOnlyList<KuroDirectoryIntegrityCheck> IntegrityChecks { get; init; }

        public required string StagingDir { get; init; }
    }



    private async Task<KuroInstallPlan> PrepareAsync(GameInstallContext context, string gameDir, CancellationToken cancellationToken)
    {
        KuroLauncherGameIndex index = await _launcherClient.GetGameIndexAsync(cancellationToken)
            ?? throw new InvalidOperationException("Wuthering Waves game config is not available.");
        bool predownload = context.Operation is GameInstallOperation.Predownload;
        KuroLauncherGameResource resource = (predownload ? index.Predownload : index.Default)
            ?? throw new InvalidOperationException(predownload ? "No predownload is available." : "Wuthering Waves game resource is not available.");
        KuroLauncherGameConfig config = resource.Config
            ?? throw new InvalidOperationException("Wuthering Waves download config is not available.");
        if (string.IsNullOrWhiteSpace(config.IndexFile) || string.IsNullOrWhiteSpace(config.BaseUrl))
        {
            throw new InvalidOperationException("Wuthering Waves download config has no index file.");
        }
        IReadOnlyList<string> cdnBases = KuroDownloadPlanner.GetCdnBases(resource);
        if (cdnBases.Count == 0)
        {
            throw new InvalidOperationException("Wuthering Waves download config has no CDN.");
        }
        string targetVersion = config.Version ?? resource.Version
            ?? throw new InvalidOperationException("Wuthering Waves download config has no version.");

        KuroResourceIndex fullIndex = await _launcherClient.GetResourceIndexAsync(cdnBases, config.IndexFile, cancellationToken)
            ?? throw new InvalidOperationException("Wuthering Waves file index is not available.");

        string? localVersion = null;
        KuroPatchPlan? patchPlan = null;
        if (context.Operation is GameInstallOperation.Update or GameInstallOperation.Predownload)
        {
            localVersion = ReadLocalVersion(gameDir)
                ?? throw new InvalidOperationException("Local game version of Wuthering Waves is unknown.");
            if (KuroDownloadPlanner.FindPatch(config, localVersion) is KuroLauncherPatchConfig patch)
            {
                KuroResourceIndex? patchIndex = await _launcherClient.GetResourceIndexAsync(cdnBases, patch.IndexFile!, cancellationToken);
                if (patchIndex is not null && KuroDownloadPlanner.IsPatchSupported(patchIndex))
                {
                    patchPlan = KuroDownloadPlanner.CreatePatchPlan(patchIndex, patch.BaseUrl!);
                }
                else
                {
                    _logger.LogWarning("Patch {local} -> {target} of Wuthering Waves is not supported, fall back to the full index.", localVersion, targetVersion);
                }
            }
            else
            {
                _logger.LogInformation("No patch from {local} to {target} of Wuthering Waves, fall back to the full index.", localVersion, targetVersion);
            }
        }

        context.LatestGameVersion = targetVersion;
        context.LocalGameVersion = localVersion;
        context.PredownloadVersion = predownload ? targetVersion : null;
        context.DownloadMode = patchPlan is not null ? GameInstallDownloadMode.Patch : GameInstallDownloadMode.SingleFile;

        var plan = new KuroInstallPlan
        {
            TargetVersion = targetVersion,
            LocalVersion = localVersion,
            CdnBases = cdnBases,
            Config = config,
            FullIndex = fullIndex,
            Patch = patchPlan,
            IntegrityChecks = KuroDownloadPlanner.ParseDirectoryIntegrityChecks(index.Experiment?.Repair?.DirectoryIntegrityCheckList),
            StagingDir = KuroDownloadPlanner.GetStagingDirectory(context.InstallPath, targetVersion),
        };
        _logger.LogInformation("""
            Prepare Wuthering Waves task finished:
            Operation: {operation}
            LocalVersion: {local}
            TargetVersion: {target}
            FullIndexFiles: {files} ({size} bytes)
            PatchDiffs: {diffs}
            PatchFiles: {patchFiles}
            PatchDownloadSize: {patchSize}
            CDN: {cdn}
            """, context.Operation, localVersion, targetVersion, fullIndex.Resource.Count, fullIndex.Resource.Sum(x => x.Size),
            patchPlan?.Diffs.Count, patchPlan?.Files.Count, patchPlan?.DownloadSize, string.Join(", ", cdnBases));
        return plan;
    }



    /// <summary>
    /// 本机版本，记在游戏目录的 launcherDownloadConfig.json
    /// </summary>
    public static string? ReadLocalVersion(string gameDir)
    {
        string path = Path.Combine(gameDir, KuroGameMapping.VersionFileName);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            using FileStream fs = File.OpenRead(path);
            using JsonDocument doc = JsonDocument.Parse(fs);
            if (doc.RootElement.TryGetProperty("version", out JsonElement element) && element.GetString() is string version && !string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
        }
        catch (JsonException) { }
        return null;
    }


    /// <summary>
    /// 写入本机版本。已有的文件只改版本号，其余字段（官方启动器自己的状态）保留。
    /// </summary>
    private void WriteLocalVersion(string gameDir, string version)
    {
        string path = Path.Combine(gameDir, KuroGameMapping.VersionFileName);
        KuroLauncherDownloadConfig config = new() { AppId = KuroGameMapping.GlobalAppId };
        if (File.Exists(path))
        {
            try
            {
                config = JsonSerializer.Deserialize<KuroLauncherDownloadConfig>(File.ReadAllText(path)) ?? config;
            }
            catch (JsonException) { }
        }
        config.Version = version;
        config.IsPreDownload = false;
        if (string.IsNullOrWhiteSpace(config.AppId))
        {
            config.AppId = KuroGameMapping.GlobalAppId;
        }
        File.WriteAllText(path, JsonSerializer.Serialize(config));
        _logger.LogInformation("Set Wuthering Waves local version to {version}: {path}", version, path);
    }



    #endregion




    #region Download



    /// <summary>
    /// 按完整清单逐个下载，已经对的文件只校验不下载。安装与修复都是这一步。
    /// </summary>
    private async Task DownloadFullAsync(GameInstallContext context, KuroInstallPlan plan, string gameDir, CancellationToken cancellationToken)
    {
        List<KuroResourceFile> files = plan.FullIndex.Resource;
        context.Progress_DownloadTotalBytes = files.Sum(x => x.Size);
        context.Progress_DownloadFinishBytes = 0;
        context.State = GameInstallState.Downloading;
        _logger.LogInformation("Wuthering Waves: download {count} files by the full index.", files.Count);
        await Parallel.ForEachAsync(files, cancellationToken, async (file, token) =>
        {
            string path = KuroDirDiffPatcher.ToFullPath(gameDir, file.Dest);
            await DownloadFileAsync(context, plan, path, plan.Config.BaseUrl!, file, token);
        });
    }



    /// <summary>
    /// 下载一个文件，一个 CDN 重试几次仍不行就换下一个
    /// </summary>
    private async Task DownloadFileAsync(GameInstallContext context, KuroInstallPlan plan, string path, string folder, KuroResourceFile file, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        foreach (string cdn in plan.CdnBases)
        {
            string url = KuroDownloadPlanner.GetFileUrl(cdn, folder, file.Dest);
            try
            {
                await _polly.ExecuteAsync(async token => await _gameInstallHelper.DownloadToFileAsync(context, path, url, file.Size, file.Md5, token), cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Download failed, try the next CDN: {url}", url);
            }
        }
        throw lastException ?? new InvalidOperationException($"No CDN for {file.Dest}.");
    }



    /// <summary>
    /// 下载补丁清单里的东西到暂存目录：差分包放 diff，普通文件放 files。
    /// 已经打完的差分包不再下载。
    /// </summary>
    private async Task DownloadPatchAsync(GameInstallContext context, KuroInstallPlan plan, CancellationToken cancellationToken)
    {
        KuroPatchPlan patch = plan.Patch!;
        HashSet<string> applied = LoadJournal(plan);
        string diffDir = Path.Combine(plan.StagingDir, DiffFolderName);
        string filesDir = Path.Combine(plan.StagingDir, FilesFolderName);
        var downloads = new List<(string Path, string Folder, KuroResourceFile File)>();
        foreach (KuroPlannedDiff diff in patch.Diffs)
        {
            if (!applied.Contains(diff.File.Dest))
            {
                downloads.Add((KuroDirDiffPatcher.ToFullPath(diffDir, diff.File.Dest), diff.Folder, diff.File));
            }
        }
        foreach (KuroPlannedFile file in patch.Files)
        {
            downloads.Add((KuroDirDiffPatcher.ToFullPath(filesDir, file.File.Dest), file.Folder, file.File));
        }

        context.Progress_DownloadTotalBytes = downloads.Sum(x => x.File.Size);
        context.Progress_DownloadFinishBytes = 0;
        context.State = GameInstallState.Downloading;
        _logger.LogInformation("Wuthering Waves: download {count} patch files ({size} bytes) to {dir}.", downloads.Count, context.Progress_DownloadTotalBytes, plan.StagingDir);
        await Parallel.ForEachAsync(downloads, cancellationToken, async (item, token) =>
        {
            await DownloadFileAsync(context, plan, item.Path, item.Folder, item.File, token);
        });
    }



    #endregion




    #region Update



    /// <summary>
    /// 按补丁更新：下载 → 逐个打差分 → 放入热更新文件 → 删掉废弃文件。
    /// 某个差分打不上时（本机文件被改过、格式认不出来），那一组改为下载完整的新文件。
    /// </summary>
    private async Task UpdateByPatchAsync(GameInstallContext context, KuroInstallPlan plan, string gameDir, CancellationToken cancellationToken)
    {
        KuroPatchPlan patch = plan.Patch!;
        await DownloadPatchAsync(context, plan, cancellationToken);

        HashSet<string> applied = LoadJournal(plan);
        string diffDir = Path.Combine(plan.StagingDir, DiffFolderName);
        string workDir = Path.Combine(plan.StagingDir, WorkFolderName);
        var failed = new List<KuroPlannedDiff>();

        context.State = GameInstallState.Merging;
        context.Progress_Percent = 0;
        double totalBytes = Math.Max(1, patch.Diffs.Sum(x => x.Group.DstFiles.Sum(y => y.Size)));
        foreach (KuroPlannedDiff diff in patch.Diffs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double ratio = diff.Group.DstFiles.Sum(x => x.Size) / totalBytes;
            if (!applied.Contains(diff.File.Dest))
            {
                string diffPath = KuroDirDiffPatcher.ToFullPath(diffDir, diff.File.Dest);
                try
                {
                    await _patcher.ApplyAsync(context, gameDir, diffPath, diff.Group, workDir, cancellationToken);
                    applied.Add(diff.File.Dest);
                    SaveJournal(plan, applied);
                    File.Delete(diffPath);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 宁可多下载，也不留下打坏的文件。patcher 校验通过前不会动游戏目录，旧文件还在。
                    _logger.LogWarning(ex, "Apply {diff} failed, download {count} new files instead.", diff.File.Dest, diff.Group.DstFiles.Count);
                    failed.Add(diff);
                }
            }
            context.Progress_Percent = Math.Min(1, context.Progress_Percent + ratio);
        }

        if (failed.Count > 0)
        {
            await DownloadLatestFilesAsync(context, plan, gameDir, failed.SelectMany(x => x.Group.DstFiles).Select(x => x.Dest), cancellationToken);
            // 下载完才记下来：中途断掉的话，下次继续时还会再试一次差分，而不是当成已经好了
            foreach (KuroPlannedDiff diff in failed)
            {
                applied.Add(diff.File.Dest);
                string diffPath = KuroDirDiffPatcher.ToFullPath(diffDir, diff.File.Dest);
                if (File.Exists(diffPath))
                {
                    File.Delete(diffPath);
                }
            }
            SaveJournal(plan, applied);
        }

        // 热更新文件一定要在差分之后放：它们有可能正是差分要读的旧文件
        string filesDir = Path.Combine(plan.StagingDir, FilesFolderName);
        foreach (KuroPlannedFile file in patch.Files)
        {
            string source = KuroDirDiffPatcher.ToFullPath(filesDir, file.File.Dest);
            string target = KuroDirDiffPatcher.ToFullPath(gameDir, file.File.Dest);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                File.SetAttributes(target, FileAttributes.Normal);
            }
            File.Move(source, target, true);
        }

        int deleted = 0;
        foreach (string file in patch.DeleteFiles)
        {
            string path = KuroDirDiffPatcher.ToFullPath(gameDir, file);
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                deleted++;
            }
        }
        _logger.LogInformation("Wuthering Waves: updated {local} -> {target} by patch, {failed} diffs fell back to full download, {deleted} files deleted.",
                               plan.LocalVersion, plan.TargetVersion, failed.Count, deleted);
    }



    /// <summary>
    /// 没有补丁可用时按完整清单比对：先收回预下载时放在暂存目录的文件，其余逐个校验、坏的重下。
    /// 这与修复是同一件事，所以同样要清掉多余的 pak。
    /// </summary>
    private async Task UpdateByFullIndexAsync(GameInstallContext context, KuroInstallPlan plan, string gameDir, CancellationToken cancellationToken)
    {
        string filesDir = Path.Combine(plan.StagingDir, FilesFolderName);
        if (Directory.Exists(filesDir))
        {
            foreach (KuroResourceFile file in plan.FullIndex.Resource)
            {
                string source = KuroDirDiffPatcher.ToFullPath(filesDir, file.Dest);
                if (await _gameInstallHelper.CheckFileMD5Async(context, source, file.Size, file.Md5, cancellationToken))
                {
                    string target = KuroDirDiffPatcher.ToFullPath(gameDir, file.Dest);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(source, target, true);
                }
            }
        }
        await DownloadFullAsync(context, plan, gameDir, cancellationToken);
        DeleteRedundantFiles(plan, gameDir);
    }



    /// <summary>
    /// 按完整清单下载指定的文件，用于差分打不上时的补救
    /// </summary>
    private async Task DownloadLatestFilesAsync(GameInstallContext context, KuroInstallPlan plan, string gameDir, IEnumerable<string> dests, CancellationToken cancellationToken)
    {
        var latest = new Dictionary<string, KuroResourceFile>(StringComparer.OrdinalIgnoreCase);
        foreach (KuroResourceFile file in plan.FullIndex.Resource)
        {
            latest.TryAdd(file.Dest, file);
        }
        var files = dests.Distinct(StringComparer.OrdinalIgnoreCase)
                         .Select(x => latest.GetValueOrDefault(x))
                         .OfType<KuroResourceFile>()
                         .ToList();
        GameInstallState state = context.State;
        context.State = GameInstallState.Downloading;
        context.Progress_DownloadTotalBytes += files.Sum(x => x.Size);
        await Parallel.ForEachAsync(files, cancellationToken, async (file, token) =>
        {
            string path = KuroDirDiffPatcher.ToFullPath(gameDir, file.Dest);
            await DownloadFileAsync(context, plan, path, plan.Config.BaseUrl!, file, token);
        });
        context.State = state;
    }



    private static HashSet<string> LoadJournal(KuroInstallPlan plan)
    {
        string path = Path.Combine(plan.StagingDir, JournalFileName);
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) is List<string> list)
            {
                return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (JsonException) { }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }


    private static void SaveJournal(KuroInstallPlan plan, HashSet<string> applied)
    {
        Directory.CreateDirectory(plan.StagingDir);
        File.WriteAllText(Path.Combine(plan.StagingDir, JournalFileName), JsonSerializer.Serialize(applied.ToList()));
    }



    private async Task WritePredownloadMarkerAsync(KuroInstallPlan plan, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(plan.StagingDir);
        var marker = new KuroPredownloadMarker { LocalVersion = plan.LocalVersion ?? "", TargetVersion = plan.TargetVersion };
        await File.WriteAllTextAsync(Path.Combine(plan.StagingDir, KuroDownloadPlanner.PredownloadMarkerFileName), JsonSerializer.Serialize(marker), cancellationToken);
        _logger.LogInformation("Wuthering Waves: predownload {local} -> {target} finished.", plan.LocalVersion, plan.TargetVersion);
    }


    private void DeleteStaging(KuroInstallPlan plan)
    {
        try
        {
            if (Directory.Exists(plan.StagingDir))
            {
                Directory.Delete(plan.StagingDir, true);
            }
            string root = Path.GetDirectoryName(plan.StagingDir)!;
            if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any())
            {
                Directory.Delete(root);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Delete staging directory {dir}", plan.StagingDir);
        }
    }



    #endregion




    #region Repair



    /// <summary>
    /// 删掉清单上没有的 pak 与 sig。
    /// <para/>
    /// 虚幻引擎会挂载 Paks 目录下所有的 pak，旧版本留下的 pak 会盖掉新内容，
    /// 官方的修复也是按 experiment.repair.directoryIntegrityCheckList 这样清。
    /// 这同样会删掉放在这里的模组，与官方行为一致。
    /// </summary>
    private void DeleteRedundantFiles(KuroInstallPlan plan, string gameDir)
    {
        var known = new HashSet<string>(plan.FullIndex.Resource.Select(x => x.Dest.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
        foreach (KuroDirectoryIntegrityCheck check in plan.IntegrityChecks)
        {
            string dir;
            try
            {
                dir = KuroDirDiffPatcher.ToFullPath(gameDir, check.Dir!);
            }
            catch (InvalidDataException)
            {
                continue;
            }
            if (!Directory.Exists(dir))
            {
                continue;
            }
            var exts = new HashSet<string>(check.Exts!.Select(x => "." + x.TrimStart('.')), StringComparer.OrdinalIgnoreCase);
            SearchOption option = check.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (string file in Directory.EnumerateFiles(dir, "*", option).ToList())
            {
                if (!exts.Contains(Path.GetExtension(file)))
                {
                    continue;
                }
                string relative = Path.GetRelativePath(gameDir, file).Replace('\\', '/');
                if (!known.Contains(relative))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    _logger.LogInformation("Wuthering Waves: delete redundant file {file}", relative);
                }
            }
        }
    }



    #endregion



}

