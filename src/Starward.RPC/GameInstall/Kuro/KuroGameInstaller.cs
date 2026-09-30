using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Starward.Core.Games;
using Starward.Core.Games.Kuro;
using Starward.Core.Launcher.Kuro;
using Starward.Core.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// 3.7.0 起游戏分成共用包与极致、高清、流畅各档专属的资源包（见 <see cref="KuroResourceTier"/>）。
/// 上面每一步都改成逐个资源包做：只装 HD 时照旧走旧版配置（一个整包），牵涉其他分级时走新启动器的分级配置，
/// 规则见 <see cref="KuroResourcePackPlanner.ChooseSource"/>。安装时带上想要的分级就是「变更分级」：
/// 缺的包下载，不要的分级目录在下载完之后删掉。
/// <para/>
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
                await DownloadFullAsync(context, plan, plan.Packs, gameDir, cancellationToken);
                // 新的分级都装好了才删不要的：中途失败的话，至少还有原来那档能玩
                DeleteRemovedTiers(plan, gameDir);
                WriteLocalVersion(gameDir, plan.TargetVersion);
                break;
            case GameInstallOperation.Repair:
                await DownloadFullAsync(context, plan, plan.Packs, gameDir, cancellationToken);
                DeleteRedundantFiles(plan, gameDir);
                WriteLocalVersion(gameDir, plan.TargetVersion);
                break;
            case GameInstallOperation.Update:
                await UpdateAsync(context, plan, gameDir, cancellationToken);
                WriteLocalVersion(gameDir, plan.TargetVersion);
                DeleteStaging(plan);
                break;
            case GameInstallOperation.Predownload:
                if (plan.Packs.All(x => x.Patch is null))
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
        public required KuroDownloadSource Source { get; init; }

        public required string TargetVersion { get; init; }

        public string? LocalVersion { get; init; }

        public required IReadOnlyList<string> CdnBases { get; init; }

        /// <summary>
        /// 要处理的资源包，共用包在前。旧版配置只有一个整包。
        /// </summary>
        public required IReadOnlyList<KuroPackPlan> Packs { get; init; }

        public required IReadOnlyList<KuroDirectoryIntegrityCheck> IntegrityChecks { get; init; }

        /// <summary>
        /// 这个目标版本的暂存目录，预下载完成的标记放在这里
        /// </summary>
        public required string StagingRoot { get; init; }

        /// <summary>
        /// 处理完之后本机应有的分级
        /// </summary>
        public required IReadOnlyList<string> Tiers { get; init; }

        /// <summary>
        /// 安装（变更分级）时本机有、但不再要的分级，全部下载完之后删掉
        /// </summary>
        public IReadOnlyList<string> RemovedTiers { get; init; } = [];

        /// <summary>
        /// 在已是最新版本的游戏上变更分级：原有的文件大小对就算数，不再逐个算 MD5。
        /// 否则加装一档流畅之前要先把 40 GB 的共用文件整个读一遍。
        /// </summary>
        public bool TrustExistingFiles { get; init; }
    }


    private sealed class KuroPackPlan
    {
        /// <summary>
        /// 资源包名，旧版配置的整包为空字符串
        /// </summary>
        public required string Name { get; init; }

        public required KuroLauncherGameConfig Config { get; init; }

        public required KuroResourceIndex FullIndex { get; init; }

        public KuroPatchPlan? Patch { get; init; }

        /// <summary>
        /// 补丁的暂存目录。旧版配置的整包就是 <see cref="KuroInstallPlan.StagingRoot"/>（与以前一致，
        /// 升级 Starward 之前开始的预下载接得上），分级配置每个包一个子目录，免得差分包同名打架。
        /// </summary>
        public required string StagingDir { get; init; }

        public string Label => string.IsNullOrEmpty(Name) ? "default" : Name;
    }



    private async Task<KuroInstallPlan> PrepareAsync(GameInstallContext context, string gameDir, CancellationToken cancellationToken)
    {
        bool predownload = context.Operation is GameInstallOperation.Predownload;
        IReadOnlyList<string> installed = KuroResourceTier.GetInstalledTiers(gameDir);
        string? localVersion = ReadLocalVersion(gameDir);
        if (context.Operation is GameInstallOperation.Update or GameInstallOperation.Predownload && localVersion is null)
        {
            throw new InvalidOperationException("Local game version of Wuthering Waves is unknown.");
        }
        // 安装时照界面选的；其余操作照本机装了的。3.7.0 以前的安装没有分级目录，更新上来就是官方默认的 HD。
        IReadOnlyList<string> tiers = context.Operation is GameInstallOperation.Install && KuroResourceTier.Parse(context.ResourceTiers) is { Count: > 0 } requested
                                    ? requested
                                    : installed.Count > 0 ? installed : [KuroResourceTier.Default];

        // 两份配置都问，任何一份读不到都不算错：另一份可能还给得了
        KuroLauncherGameIndex? legacy = await TryGetAsync(_launcherClient.GetGameIndexAsync, "legacy game index", cancellationToken);
        KuroOfficialGameIndex? tiered = await TryGetAsync(_launcherClient.GetOfficialGameIndexAsync, "tiered game index", cancellationToken);

        KuroLauncherGameResource? legacyResource = predownload ? legacy?.Predownload : legacy?.Default;
        if (predownload && legacy?.PredownloadSwitch != 1)
        {
            legacyResource = null;
        }
        string? legacyVersion = legacyResource?.Config is { IndexFile.Length: > 0, BaseUrl.Length: > 0 } legacyConfig
                              ? legacyConfig.Version ?? legacyResource.Version
                              : null;

        Dictionary<string, KuroLauncherGameConfig>? tieredPacks = tiered?.ResourcePacks;
        Dictionary<string, KuroResourceBundle>? tieredBundles = tiered?.Bundles;
        List<KuroLauncherCdn>? tieredCdn = tiered?.CdnList;
        if (predownload)
        {
            KuroOfficialPredownload? p = tiered is not null && tiered.Config?.PredownloadSwitch == 1 ? KuroResourcePackPlanner.GetPredownload(tiered) : null;
            tieredPacks = p?.ResourcePacks;
            tieredBundles = p?.Bundles;
            tieredCdn = p?.CdnList;
        }
        IReadOnlyList<KuroResourcePack>? packs = KuroResourcePackPlanner.GetPacks(tieredPacks, tieredBundles, tiers);
        string? tieredVersion = packs is null ? null : KuroResourcePackPlanner.GetVersion(tieredPacks);

        if (KuroResourcePackPlanner.ChooseSource(tiers, legacyVersion, tieredVersion) is not KuroDownloadSource source)
        {
            if (predownload)
            {
                throw new InvalidOperationException("No predownload is available.");
            }
            // 要的分级只有分级配置给得了，而它现在读不到（或官方改了格式）
            throw new NotSupportedException(string.Format(CoreLang.KuroInstall_UnsupportedResourceTiers, string.Join(", ", tiers.Select(x => x.ToUpperInvariant()))));
        }

        string targetVersion;
        IReadOnlyList<string> cdnBases;
        IReadOnlyList<KuroDirectoryIntegrityCheck> integrityChecks;
        string stagingRoot;
        List<(string Name, KuroLauncherGameConfig Config)> configs;
        if (source is KuroDownloadSource.Legacy)
        {
            targetVersion = legacyVersion!;
            cdnBases = KuroDownloadPlanner.GetCdnBases(legacy!, legacyResource!);
            integrityChecks = KuroDownloadPlanner.ParseDirectoryIntegrityChecks(legacy!.Experiment?.Repair?.DirectoryIntegrityCheckList);
            configs = [("", legacyResource!.Config!)];
        }
        else
        {
            targetVersion = tieredVersion!;
            cdnBases = KuroDownloadPlanner.GetCdnBases(tieredCdn is { Count: > 0 } ? tieredCdn : tiered?.CdnList);
            integrityChecks = KuroDownloadPlanner.ParseDirectoryIntegrityChecks(tiered!.Config?.Experiment?.Repair?.DirectoryIntegrityCheckList);
            configs = packs!.Select(x => (x.Name, x.Config)).ToList();
        }
        if (cdnBases.Count == 0)
        {
            throw new InvalidOperationException("Wuthering Waves download config has no CDN.");
        }
        // 在已装好的游戏上变更分级，只有本机已是目标版本时才成立。版本不同时照做就是不打补丁地整包重下，
        // 旧版本留下的 pak 也不会清掉；分级配置落后于旧版配置时（只装 HD 的已照旧版配置更新上去）
        // 还会把共用文件盖回旧版本。界面只在已是最新版本时才开放，这里防它读到的版本信息过时。
        if (context.Operation is GameInstallOperation.Install
            && installed.Count > 0
            && localVersion is not null
            && !string.Equals(localVersion, targetVersion, StringComparison.OrdinalIgnoreCase)
            && !installed.ToHashSet().SetEquals(tiers))
        {
            throw new InvalidOperationException(string.Format(CoreLang.KuroInstall_UpdateBeforeChangingResourceTiers, localVersion, targetVersion));
        }
        stagingRoot = KuroDownloadPlanner.GetStagingDirectory(context.InstallPath, targetVersion);

        var packPlans = new List<KuroPackPlan>(configs.Count);
        foreach ((string name, KuroLauncherGameConfig config) in configs)
        {
            KuroResourceIndex fullIndex = await _launcherClient.GetResourceIndexAsync(cdnBases, config.IndexFile!, cancellationToken)
                ?? throw new InvalidOperationException($"Wuthering Waves file index of {(name.Length > 0 ? name : "default")} is not available.");
            KuroPatchPlan? patchPlan = null;
            if (context.Operation is GameInstallOperation.Update or GameInstallOperation.Predownload)
            {
                if (KuroDownloadPlanner.FindPatch(config, localVersion) is KuroLauncherPatchConfig patch)
                {
                    KuroResourceIndex? patchIndex = await _launcherClient.GetResourceIndexAsync(cdnBases, patch.IndexFile!, cancellationToken);
                    if (patchIndex is not null && KuroDownloadPlanner.IsPatchSupported(patchIndex))
                    {
                        patchPlan = KuroDownloadPlanner.CreatePatchPlan(patchIndex, patch.BaseUrl!);
                    }
                    else
                    {
                        _logger.LogWarning("Patch {local} -> {target} of Wuthering Waves ({pack}) is not supported, fall back to the full index.", localVersion, targetVersion, name);
                    }
                }
                else
                {
                    _logger.LogInformation("No patch from {local} to {target} of Wuthering Waves ({pack}), fall back to the full index.", localVersion, targetVersion, name);
                }
            }
            packPlans.Add(new KuroPackPlan
            {
                Name = name,
                Config = config,
                FullIndex = fullIndex,
                Patch = patchPlan,
                StagingDir = name.Length > 0 ? Path.Combine(stagingRoot, name) : stagingRoot,
            });
        }
        if (source is KuroDownloadSource.Legacy)
        {
            EnsureTiersCovered(tiers, packPlans[0].FullIndex);
        }

        context.LatestGameVersion = targetVersion;
        context.LocalGameVersion = localVersion;
        context.PredownloadVersion = predownload ? targetVersion : null;
        context.DownloadMode = packPlans.Any(x => x.Patch is not null) ? GameInstallDownloadMode.Patch : GameInstallDownloadMode.SingleFile;

        var plan = new KuroInstallPlan
        {
            Source = source,
            TargetVersion = targetVersion,
            LocalVersion = localVersion,
            CdnBases = cdnBases,
            Packs = packPlans.AsReadOnly(),
            IntegrityChecks = integrityChecks,
            StagingRoot = stagingRoot,
            Tiers = tiers,
            RemovedTiers = context.Operation is GameInstallOperation.Install ? installed.Except(tiers).ToList().AsReadOnly() : [],
            TrustExistingFiles = context.Operation is GameInstallOperation.Install
                                 && installed.Count > 0
                                 && string.Equals(localVersion, targetVersion, StringComparison.OrdinalIgnoreCase),
        };
        _logger.LogInformation("""
            Prepare Wuthering Waves task finished:
            Operation: {operation}
            Source: {source}
            Tiers: {tiers} (installed: {installed}, removing: {removed})
            LocalVersion: {local}
            TargetVersion: {target}
            Packs: {packs}
            FullIndexFiles: {files} ({size} bytes)
            PatchDiffs: {diffs}
            PatchFiles: {patchFiles}
            PatchDownloadSize: {patchSize}
            TrustExistingFiles: {trust}
            CDN: {cdn}
            """, context.Operation, source, string.Join(",", tiers), string.Join(",", installed), string.Join(",", plan.RemovedTiers),
            localVersion, targetVersion, string.Join(", ", packPlans.Select(x => x.Label)),
            packPlans.Sum(x => x.FullIndex.Resource.Count), packPlans.Sum(x => x.FullIndex.Resource.Sum(y => y.Size)),
            packPlans.Sum(x => x.Patch?.Diffs.Count ?? 0), packPlans.Sum(x => x.Patch?.Files.Count ?? 0), packPlans.Sum(x => x.Patch?.DownloadSize ?? 0),
            plan.TrustExistingFiles, string.Join(", ", cdnBases));
        return plan;
    }



    /// <summary>
    /// 读一份配置，失败时记下来并返回 null
    /// </summary>
    private async Task<T?> TryGetAsync<T>(Func<CancellationToken, Task<T?>> get, string name, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await get(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Get Wuthering Waves {name}", name);
            return null;
        }
    }



    /// <summary>
    /// 旧版配置只有 HD，要处理的分级都得在清单里，否则不动手。
    /// <para/>
    /// 选来源时已经只让只装 HD 的走旧版配置，这里是防官方哪天把旧版配置改成别的分级：
    /// 照着缺了某档的清单更新，那一档会留在旧版本，带着它的参数启动就会出错。
    /// </summary>
    private void EnsureTiersCovered(IReadOnlyList<string> tiers, KuroResourceIndex index)
    {
        IReadOnlyList<string> covered = KuroResourceTier.GetTiersInIndex(index.Resource.Select(x => x.Dest));
        List<string> missing = tiers.Except(covered).ToList();
        if (missing.Count > 0)
        {
            _logger.LogWarning("Wuthering Waves needs resource tiers {tiers}, but the legacy index only covers {covered}.", tiers, covered);
            throw new NotSupportedException(string.Format(CoreLang.KuroInstall_UnsupportedResourceTiers, string.Join(", ", missing.Select(x => x.ToUpperInvariant()))));
        }
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
    /// 写入本机版本。已有的文件只改版本号与预下载标记，其余字段原样保留：
    /// 官方新启动器可能在这里记了别的东西（例如装了哪几档），整份覆盖会把它们抹掉。
    /// </summary>
    private void WriteLocalVersion(string gameDir, string version)
    {
        string path = Path.Combine(gameDir, KuroGameMapping.VersionFileName);
        JsonObject? config = null;
        if (File.Exists(path))
        {
            try
            {
                config = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            }
            catch (JsonException) { }
        }
        config ??= new JsonObject
        {
            ["version"] = "",
            ["reUseVersion"] = "",
            ["state"] = "",
            ["isPreDownload"] = false,
            ["appId"] = KuroGameMapping.GlobalAppId,
        };
        config["version"] = version;
        config["isPreDownload"] = false;
        if (config["appId"] is not JsonValue appId || !appId.TryGetValue(out string? value) || string.IsNullOrWhiteSpace(value))
        {
            config["appId"] = KuroGameMapping.GlobalAppId;
        }
        File.WriteAllText(path, config.ToJsonString());
        _logger.LogInformation("Set Wuthering Waves local version to {version}: {path}", version, path);
    }



    #endregion




    #region Download



    /// <summary>
    /// 按完整清单逐个下载，已经对的文件只校验不下载。安装与修复都是这一步。
    /// </summary>
    private async Task DownloadFullAsync(GameInstallContext context, KuroInstallPlan plan, IEnumerable<KuroPackPlan> packs, string gameDir, CancellationToken cancellationToken)
    {
        var files = packs.SelectMany(pack => pack.FullIndex.Resource.Select(file => (Pack: pack, File: file))).ToList();
        context.Progress_DownloadTotalBytes = files.Sum(x => x.File.Size);
        context.Progress_DownloadFinishBytes = 0;
        context.State = GameInstallState.Downloading;
        _logger.LogInformation("Wuthering Waves: download {count} files by the full index.", files.Count);
        await Parallel.ForEachAsync(files, cancellationToken, async (item, token) =>
        {
            string path = KuroDirDiffPatcher.ToFullPath(gameDir, item.File.Dest);
            if (plan.TrustExistingFiles && IsSameSize(path, item.File.Size))
            {
                Interlocked.Add(ref context._progress_DownloadFinishBytes, item.File.Size);
                context.VerifiedFiles[path] = item.File.Size;
                return;
            }
            await DownloadFileAsync(context, plan, path, item.Pack.Config.BaseUrl!, item.File, token);
        });
    }


    private static bool IsSameSize(string path, long size)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length == size;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
    /// 下载每个资源包补丁清单里的东西到各自的暂存目录：差分包放 diff，普通文件放 files。
    /// 已经打完的差分包不再下载。
    /// </summary>
    private async Task DownloadPatchAsync(GameInstallContext context, KuroInstallPlan plan, CancellationToken cancellationToken)
    {
        var downloads = new List<(string Path, string Folder, KuroResourceFile File)>();
        foreach (KuroPackPlan pack in plan.Packs)
        {
            if (pack.Patch is not KuroPatchPlan patch)
            {
                continue;
            }
            HashSet<string> applied = LoadJournal(pack);
            string diffDir = Path.Combine(pack.StagingDir, DiffFolderName);
            string filesDir = Path.Combine(pack.StagingDir, FilesFolderName);
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
        }

        context.Progress_DownloadTotalBytes = downloads.Sum(x => x.File.Size);
        context.Progress_DownloadFinishBytes = 0;
        context.State = GameInstallState.Downloading;
        _logger.LogInformation("Wuthering Waves: download {count} patch files ({size} bytes) to {dir}.", downloads.Count, context.Progress_DownloadTotalBytes, plan.StagingRoot);
        await Parallel.ForEachAsync(downloads, cancellationToken, async (item, token) =>
        {
            await DownloadFileAsync(context, plan, item.Path, item.Folder, item.File, token);
        });
    }



    #endregion




    #region Update



    /// <summary>
    /// 更新：有补丁的资源包打补丁，没有的按完整清单比对。
    /// 所有补丁先一起下载（与预下载相同，预下载过的这一步只校验），再逐个资源包处理。
    /// </summary>
    private async Task UpdateAsync(GameInstallContext context, KuroInstallPlan plan, string gameDir, CancellationToken cancellationToken)
    {
        List<KuroPackPlan> patched = plan.Packs.Where(x => x.Patch is not null).ToList();
        List<KuroPackPlan> unpatched = plan.Packs.Where(x => x.Patch is null).ToList();
        if (patched.Count > 0)
        {
            await DownloadPatchAsync(context, plan, cancellationToken);
            context.State = GameInstallState.Merging;
            context.Progress_Percent = 0;
            double totalBytes = Math.Max(1, patched.Sum(x => x.Patch!.Diffs.Sum(y => y.Group.DstFiles.Sum(z => z.Size))));
            foreach (KuroPackPlan pack in patched)
            {
                await UpdatePackByPatchAsync(context, plan, pack, gameDir, totalBytes, cancellationToken);
            }
        }
        if (unpatched.Count > 0)
        {
            await UpdateByFullIndexAsync(context, plan, unpatched, gameDir, cancellationToken);
        }
    }



    /// <summary>
    /// 按补丁更新一个资源包：逐个打差分 → 放入热更新文件 → 删掉废弃文件。
    /// 某个差分打不上时（本机文件被改过、格式认不出来），那一组改为下载完整的新文件。
    /// </summary>
    private async Task UpdatePackByPatchAsync(GameInstallContext context, KuroInstallPlan plan, KuroPackPlan pack, string gameDir, double totalBytes, CancellationToken cancellationToken)
    {
        KuroPatchPlan patch = pack.Patch!;
        HashSet<string> applied = LoadJournal(pack);
        string diffDir = Path.Combine(pack.StagingDir, DiffFolderName);
        string workDir = Path.Combine(pack.StagingDir, WorkFolderName);
        var failed = new List<KuroPlannedDiff>();

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
                    SaveJournal(pack, applied);
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
            await DownloadLatestFilesAsync(context, plan, pack, gameDir, failed.SelectMany(x => x.Group.DstFiles).Select(x => x.Dest), cancellationToken);
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
            SaveJournal(pack, applied);
        }

        // 热更新文件一定要在差分之后放：它们有可能正是差分要读的旧文件
        string filesDir = Path.Combine(pack.StagingDir, FilesFolderName);
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
        _logger.LogInformation("Wuthering Waves: updated {pack} {local} -> {target} by patch, {failed} diffs fell back to full download, {deleted} files deleted.",
                               pack.Label, plan.LocalVersion, plan.TargetVersion, failed.Count, deleted);
    }



    /// <summary>
    /// 没有补丁可用的资源包按完整清单比对：先收回预下载时放在暂存目录的文件，其余逐个校验、坏的重下。
    /// 这与修复是同一件事，所以同样要清掉多余的 pak。
    /// </summary>
    private async Task UpdateByFullIndexAsync(GameInstallContext context, KuroInstallPlan plan, IReadOnlyList<KuroPackPlan> packs, string gameDir, CancellationToken cancellationToken)
    {
        foreach (KuroPackPlan pack in packs)
        {
            string filesDir = Path.Combine(pack.StagingDir, FilesFolderName);
            if (!Directory.Exists(filesDir))
            {
                continue;
            }
            foreach (KuroResourceFile file in pack.FullIndex.Resource)
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
        await DownloadFullAsync(context, plan, packs, gameDir, cancellationToken);
        DeleteRedundantFiles(plan, gameDir);
    }



    /// <summary>
    /// 按资源包的完整清单下载指定的文件，用于差分打不上时的补救
    /// </summary>
    private async Task DownloadLatestFilesAsync(GameInstallContext context, KuroInstallPlan plan, KuroPackPlan pack, string gameDir, IEnumerable<string> dests, CancellationToken cancellationToken)
    {
        var latest = new Dictionary<string, KuroResourceFile>(StringComparer.OrdinalIgnoreCase);
        foreach (KuroResourceFile file in pack.FullIndex.Resource)
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
            await DownloadFileAsync(context, plan, path, pack.Config.BaseUrl!, file, token);
        });
        context.State = state;
    }



    private static HashSet<string> LoadJournal(KuroPackPlan pack)
    {
        string path = Path.Combine(pack.StagingDir, JournalFileName);
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


    private static void SaveJournal(KuroPackPlan pack, HashSet<string> applied)
    {
        Directory.CreateDirectory(pack.StagingDir);
        File.WriteAllText(Path.Combine(pack.StagingDir, JournalFileName), JsonSerializer.Serialize(applied.ToList()));
    }



    private async Task WritePredownloadMarkerAsync(KuroInstallPlan plan, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(plan.StagingRoot);
        var marker = new KuroPredownloadMarker { LocalVersion = plan.LocalVersion ?? "", TargetVersion = plan.TargetVersion };
        await File.WriteAllTextAsync(Path.Combine(plan.StagingRoot, KuroDownloadPlanner.PredownloadMarkerFileName), JsonSerializer.Serialize(marker), cancellationToken);
        _logger.LogInformation("Wuthering Waves: predownload {local} -> {target} finished.", plan.LocalVersion, plan.TargetVersion);
    }


    private void DeleteStaging(KuroInstallPlan plan)
    {
        try
        {
            if (Directory.Exists(plan.StagingRoot))
            {
                Directory.Delete(plan.StagingRoot, true);
            }
            string root = Path.GetDirectoryName(plan.StagingRoot)!;
            if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any())
            {
                Directory.Delete(root);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Delete staging directory {dir}", plan.StagingRoot);
        }
    }



    #endregion




    #region Repair



    /// <summary>
    /// 删掉清单上没有的 pak 与 sig。
    /// <para/>
    /// 虚幻引擎会挂载 Paks 目录下所有的 pak，旧版本留下的 pak 会盖掉新内容，
    /// 官方的修复也是按 experiment.repair.directoryIntegrityCheckList 这样清。
    /// 这同样会删掉放在这里的模组，与官方行为一致。清单是所有资源包合起来的，
    /// 目前官方只清 Paks（共用包），各档的目录不在其中。
    /// </summary>
    private void DeleteRedundantFiles(KuroInstallPlan plan, string gameDir)
    {
        var known = new HashSet<string>(plan.Packs.SelectMany(x => x.FullIndex.Resource).Select(x => x.Dest.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
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



    /// <summary>
    /// 变更分级时删掉不要的那几档，整个 Client\Content\{分级} 目录。
    /// 只删认得的分级目录，而且一定不是这次要留下的。
    /// </summary>
    private void DeleteRemovedTiers(KuroInstallPlan plan, string gameDir)
    {
        foreach (string tier in plan.RemovedTiers)
        {
            if (plan.Tiers.Contains(tier) || KuroResourceTier.Normalize(tier) is not string normalized)
            {
                continue;
            }
            string dir = KuroDirDiffPatcher.ToFullPath(gameDir, KuroResourceTier.GetContentFolder(normalized));
            if (!Directory.Exists(dir))
            {
                continue;
            }
            foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(dir, true);
            _logger.LogInformation("Wuthering Waves: removed resource tier {tier} ({dir})", normalized, dir);
        }
    }



    #endregion



}
