using System.Text.Json;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 把鸣潮官方的下载配置整理成「要下载哪些文件、怎么打补丁」。
/// <para/>
/// 全是纯计算，不碰网络与磁盘，好单独测试；真正下载与打补丁在 RPC 进程里。
/// </summary>
public static class KuroDownloadPlanner
{

    /// <summary>
    /// Starward 的暂存目录，放在官方启动器的安装根目录下（与游戏目录同层），
    /// 官方的修复只检查游戏目录里的 Paks，不会去动它
    /// </summary>
    public const string StagingFolderName = "starward_patch";


    /// <summary>
    /// 预下载完成的标记，放在该版本的暂存目录里
    /// </summary>
    public const string PredownloadMarkerFileName = "predownload.json";


    /// <summary>
    /// 某个目标版本的暂存目录
    /// </summary>
    /// <param name="installPath">官方启动器的安装根目录，游戏本体在其下的 Wuthering Waves Game</param>
    public static string GetStagingDirectory(string installPath, string targetVersion) => Path.Combine(installPath, StagingFolderName, targetVersion);


    /// <summary>
    /// 预下载是否已经完成：标记存在，且记下的本机版本仍是现在的本机版本
    /// （预下载之后又用官方启动器更新过的话，暂存的补丁就对不上了）
    /// </summary>
    public static bool IsPredownloadFinished(string installPath, string localVersion, string targetVersion)
    {
        string path = Path.Combine(GetStagingDirectory(installPath, targetVersion), PredownloadMarkerFileName);
        if (!File.Exists(path))
        {
            return false;
        }
        try
        {
            KuroPredownloadMarker? marker = JsonSerializer.Deserialize(File.ReadAllText(path), KuroLauncherJsonContext.Default.KuroPredownloadMarker);
            return marker is not null
                && string.Equals(marker.LocalVersion, localVersion, StringComparison.OrdinalIgnoreCase)
                && string.Equals(marker.TargetVersion, targetVersion, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return false;
        }
    }


    /// <summary>
    /// 下载用的 CDN 根地址，权重高的在前，权重为 0 的备用 CDN 排最后。
    /// 每个地址都以斜杠结尾。
    /// </summary>
    public static IReadOnlyList<string> GetCdnBases(KuroLauncherGameResource resource)
    {
        return (resource.CdnList ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Url))
            .OrderByDescending(x => x.Priority > 0)
            .ThenByDescending(x => x.Priority)
            .Select(x => x.Url!.TrimEnd('/') + "/")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }


    /// <summary>
    /// 拼接地址，两边多出来或少掉的斜杠都会被整理成一个
    /// </summary>
    public static string Combine(string baseUrl, string relative)
    {
        return baseUrl.TrimEnd('/') + "/" + relative.TrimStart('/');
    }


    /// <summary>
    /// 清单里一个文件的下载地址。
    /// <para/>
    /// 文件路径里有空格（<c>Wuthering Waves.exe</c>），逐段转义，目录分隔符保留。
    /// </summary>
    /// <param name="cdnBase">见 <see cref="GetCdnBases"/></param>
    /// <param name="folder">下载目录，<see cref="KuroResourceFile.FromFolder"/> 或配置的 baseUrl</param>
    /// <param name="dest">文件的相对路径</param>
    public static string GetFileUrl(string cdnBase, string folder, string dest)
    {
        string escaped = string.Join('/', dest.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
        return Combine(Combine(cdnBase, folder), escaped);
    }


    /// <summary>
    /// 找出从本机版本更新上来的补丁，没有时返回 null（只能按完整清单比对）
    /// </summary>
    public static KuroLauncherPatchConfig? FindPatch(KuroLauncherGameConfig config, string? localVersion)
    {
        if (string.IsNullOrWhiteSpace(localVersion))
        {
            return null;
        }
        return config.PatchConfig?.FirstOrDefault(x => string.Equals(x.Version, localVersion, StringComparison.OrdinalIgnoreCase)
                                                    && !string.IsNullOrWhiteSpace(x.IndexFile)
                                                    && !string.IsNullOrWhiteSpace(x.BaseUrl));
    }


    /// <summary>
    /// 修复时要清理的目录。官方把它写成 JSON 字符串塞在实验开关里，解析失败就当没有。
    /// </summary>
    public static IReadOnlyList<KuroDirectoryIntegrityCheck> ParseDirectoryIntegrityChecks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            List<KuroDirectoryIntegrityCheck>? list = JsonSerializer.Deserialize(json, KuroLauncherJsonContext.Default.ListKuroDirectoryIntegrityCheck);
            return (list ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Dir) && x.Exts?.Count > 0).ToList().AsReadOnly();
        }
        catch (JsonException)
        {
            return [];
        }
    }


    /// <summary>
    /// 这份补丁能不能用 Starward 实现的方式打。
    /// <para/>
    /// 目前只实现了 <c>group</c>（<c>.krpdiff</c> 目录差分）。见过官方启动器也认得单文件的
    /// <c>.krdiff</c>，但没有遇到过带它的清单，不知道配套的说明放在哪里，
    /// 遇到时整份补丁改走完整清单比对，宁可多下载也不要打坏文件。
    /// </summary>
    public static bool IsPatchSupported(KuroResourceIndex patch)
    {
        if (patch.ApplyTypes?.Any(x => !string.Equals(x, "group", StringComparison.OrdinalIgnoreCase)) is true)
        {
            return false;
        }
        var groups = new HashSet<string>((patch.GroupInfos ?? []).Select(x => x.Dest), StringComparer.OrdinalIgnoreCase);
        foreach (KuroResourceFile file in patch.Resource)
        {
            if (file.Dest.EndsWith(KuroResourceIndex.KrdiffExtension, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (file.Dest.EndsWith(KuroResourceIndex.KrpdiffExtension, StringComparison.OrdinalIgnoreCase) && !groups.Contains(file.Dest))
            {
                return false;
            }
        }
        return true;
    }


    /// <summary>
    /// 把补丁清单拆成三类：差分包、差分之后才能放进游戏目录的普通文件、最后要删的文件。
    /// <para/>
    /// 顺序很重要：补丁清单把「差分到上一个版本」与「之后的热更新文件」放在一起，
    /// 同一个文件可能既是差分的旧文件，又会被热更新文件覆盖（3.5.2 → 3.6.1 有 57 个），
    /// 所以普通文件必须先下载到暂存目录，等差分全部打完再放回去。
    /// </summary>
    /// <param name="patch">补丁清单</param>
    /// <param name="defaultFolder">补丁配置的 baseUrl，普通文件没有 fromFolder 时用它</param>
    public static KuroPatchPlan CreatePatchPlan(KuroResourceIndex patch, string defaultFolder)
    {
        var groups = new Dictionary<string, KuroResourceGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (KuroResourceGroup group in patch.GroupInfos ?? [])
        {
            groups.TryAdd(group.Dest, group);
        }
        var diffs = new List<KuroPlannedDiff>();
        var files = new List<KuroPlannedFile>();
        foreach (KuroResourceFile file in patch.Resource)
        {
            string folder = string.IsNullOrWhiteSpace(file.FromFolder) ? defaultFolder : file.FromFolder;
            if (groups.TryGetValue(file.Dest, out KuroResourceGroup? group))
            {
                diffs.Add(new KuroPlannedDiff(file, folder, group));
            }
            else
            {
                files.Add(new KuroPlannedFile(file, folder));
            }
        }
        // 最后一个 group 一般是一大批小文件合在一起（ACE、SDK 等），顺序照清单即可
        return new KuroPatchPlan(diffs.AsReadOnly(), files.AsReadOnly(), (patch.DeleteFiles ?? []).AsReadOnly());
    }


    /// <summary>
    /// 从一个版本升到另一个版本时，新清单里哪些文件与旧清单不同，
    /// 用来在没有补丁可用时估计要下载多少。两份清单都没有时返回完整清单。
    /// </summary>
    public static IReadOnlyList<KuroResourceFile> GetChangedFiles(KuroResourceIndex latest, KuroResourceIndex? local)
    {
        if (local is null)
        {
            return latest.Resource.AsReadOnly();
        }
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KuroResourceFile file in local.Resource)
        {
            dict.TryAdd(file.Dest, file.Md5);
        }
        return latest.Resource.Where(x => !dict.TryGetValue(x.Dest, out string? md5) || !string.Equals(md5, x.Md5, StringComparison.OrdinalIgnoreCase))
                              .ToList()
                              .AsReadOnly();
    }

}



/// <summary>
/// 一次补丁更新的计划
/// </summary>
/// <param name="Diffs">要下载并应用的差分包，照清单顺序</param>
/// <param name="Files">普通文件，先下载到暂存目录，差分打完之后才放进游戏目录</param>
/// <param name="DeleteFiles">全部完成后要删掉的旧文件</param>
public sealed record KuroPatchPlan(IReadOnlyList<KuroPlannedDiff> Diffs, IReadOnlyList<KuroPlannedFile> Files, IReadOnlyList<string> DeleteFiles)
{

    /// <summary>
    /// 要下载的总字节数
    /// </summary>
    public long DownloadSize => Diffs.Sum(x => x.File.Size) + Files.Sum(x => x.File.Size);

}


/// <param name="File">清单里的差分包</param>
/// <param name="Folder">下载目录</param>
/// <param name="Group">差分包的内容说明</param>
public sealed record KuroPlannedDiff(KuroResourceFile File, string Folder, KuroResourceGroup Group);


/// <param name="File">清单里的文件</param>
/// <param name="Folder">下载目录</param>
public sealed record KuroPlannedFile(KuroResourceFile File, string Folder);
