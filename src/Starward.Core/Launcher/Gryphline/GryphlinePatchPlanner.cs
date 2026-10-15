using System.Text;
using System.Text.Json;

namespace Starward.Core.Launcher.Gryphline;

/// <summary>
/// 终末地的补丁更新与预下载要用到的计算：暂存目录、预下载标记、本机版本的判断、zip 里的路径。
/// <para/>
/// 本机版本记在加密的 config.ini 里，Starward 不解密它，而是这样判断：
/// <list type="bullet">
/// <item>本机 game_files 的 MD5 等于线上最新包的 → 本机就是最新版（这也是能预下载的情形）。</item>
/// <item>落后时拿几个候选版本号去问接口，有补丁的那几个再用明文的 patch.json
/// 比对本机旧文件的大小，对得上的就是本机版本（<see cref="ScoreLocalMatch"/>）。</item>
/// </list>
/// 除了读标记文件以外不碰网络与磁盘，好单独测试。
/// </summary>
public static class GryphlinePatchPlanner
{

    /// <summary>
    /// 暂存目录，放在 GRYPHLINK 的安装根目录下（与 games 同层），官方启动器不会去动它
    /// </summary>
    public const string StagingFolderName = "starward_patch";

    public const string PredownloadMarkerFileName = "predownload.json";

    /// <summary>
    /// 打补丁的进度，记下已经处理好的文件，中断后继续时跳过
    /// </summary>
    public const string JournalFileName = "applied.json";

    /// <summary>
    /// zip 里新的 VFS 文件的前缀，后接游戏目录里的相对路径
    /// </summary>
    public const string ZipNewFilesPrefix = "vfs_files/files/";

    /// <summary>
    /// zip 里差分的前缀，后接 patch.json 里的 patch 路径
    /// </summary>
    public const string ZipDiffPrefix = "vfs_files/vfs_patch/";

    public const string PatchInfoName = "patch.json";

    public const string VerifyFilesName = "verify_files.json";

    public const string DeleteFilesName = "delete_files.txt";

    /// <summary>
    /// 本机旧文件与 patch.json 对得上的比例达到这个值，才认定本机是补丁的起点版本。
    /// 对不上的那些文件会改为下载完整的新文件，所以不必要求全部对上
    /// </summary>
    public const double LocalMatchThreshold = 0.9;



    /// <summary>
    /// 从 <paramref name="localVersion"/> 更新到 <paramref name="targetVersion"/> 的暂存目录
    /// </summary>
    /// <param name="installPath">GRYPHLINK 的安装根目录</param>
    public static string GetStagingDirectory(string installPath, string localVersion, string targetVersion)
    {
        return Path.Combine(installPath, StagingFolderName, $"{localVersion}-{targetVersion}");
    }


    /// <summary>
    /// 分卷在暂存目录里的文件名，取地址的最后一段
    /// </summary>
    public static string GetPartFileName(string url, int index)
    {
        string name = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? Path.GetFileName(uri.AbsolutePath) : Path.GetFileName(url);
        return string.IsNullOrWhiteSpace(name) ? $"patch.zip.{index + 1:D3}" : name;
    }


    /// <summary>
    /// 预下载是否完成：标记存在，且记下的本机版本与目标版本都对得上
    /// </summary>
    public static bool IsPredownloadFinished(string installPath, string localVersion, string targetVersion)
    {
        return ReadMarker(GetStagingDirectory(installPath, localVersion, targetVersion)) is GryphlinePredownloadMarker marker
            && string.Equals(marker.LocalVersion, localVersion, StringComparison.OrdinalIgnoreCase)
            && string.Equals(marker.TargetVersion, targetVersion, StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 预下载过、目标是 <paramref name="targetVersion"/> 的那份标记，用来在正式更新时知道本机版本。没有时返回 null
    /// </summary>
    public static GryphlinePredownloadMarker? FindMarker(string installPath, string targetVersion)
    {
        string root = Path.Combine(installPath, StagingFolderName);
        if (!Directory.Exists(root))
        {
            return null;
        }
        foreach (string dir in Directory.EnumerateDirectories(root))
        {
            if (ReadMarker(dir) is GryphlinePredownloadMarker marker && string.Equals(marker.TargetVersion, targetVersion, StringComparison.OrdinalIgnoreCase))
            {
                return marker;
            }
        }
        return null;
    }


    public static string SerializeMarker(GryphlinePredownloadMarker marker)
    {
        return JsonSerializer.Serialize(marker, GryphlineLauncherJsonContext.Default.GryphlinePredownloadMarker);
    }


    private static GryphlinePredownloadMarker? ReadMarker(string stagingDir)
    {
        string path = Path.Combine(stagingDir, PredownloadMarkerFileName);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), GryphlineLauncherJsonContext.Default.GryphlinePredownloadMarker) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }



    /// <summary>
    /// 本机版本的候选，新的在前：最新版本的同一个次版本与前一个次版本里、比最新版旧的修订号 0～9。
    /// 官方只给相近的版本出补丁，更旧的照样能按文件更新。
    /// </summary>
    public static IReadOnlyList<string> GetCandidateLocalVersions(string latestVersion)
    {
        if (!Version.TryParse(latestVersion, out Version? latest) || latest.Build < 0)
        {
            return [];
        }
        var list = new List<string>();
        for (int minor = latest.Minor; minor >= Math.Max(0, latest.Minor - 1); minor--)
        {
            for (int build = 9; build >= 0; build--)
            {
                var v = new Version(latest.Major, minor, build);
                if (v < latest)
                {
                    list.Add($"{v.Major}.{v.Minor}.{v.Build}");
                }
            }
        }
        return list.AsReadOnly();
    }



    /// <summary>
    /// 本机与补丁起点版本对得上的比例（0～1）。
    /// 看的是 patch.json 里差分的旧文件与没变的文件：在本机存在、而且大小一致的算对上。
    /// 只比大小不比 MD5，几十 GB 的资源逐个算 MD5 太慢；打完补丁会校验新文件的 MD5，对不上的改为下载。
    /// </summary>
    /// <param name="getLength">VFS 目录下相对路径的文件大小，不存在时返回 null</param>
    public static double ScoreLocalMatch(GryphlinePatchInfo info, Func<string, long?> getLength)
    {
        int total = 0, matched = 0;
        foreach (GryphlinePatchFile file in info.Files ?? [])
        {
            if (file.Patch is { Count: > 0 } diffs)
            {
                foreach (GryphlinePatchDiff diff in diffs)
                {
                    total++;
                    if (getLength(diff.BaseFile) == diff.BaseSize)
                    {
                        matched++;
                    }
                }
            }
            else if (string.IsNullOrWhiteSpace(file.LocalPath))
            {
                total++;
                if (getLength(file.Name) == file.Size)
                {
                    matched++;
                }
            }
        }
        return total == 0 ? 0 : (double)matched / total;
    }



    /// <summary>
    /// verify_files.json 里一个要放进游戏目录的文件，在 zip 里的路径：
    /// 非 VFS 文件就是原路径，新的 VFS 文件在 <see cref="ZipNewFilesPrefix"/> 下
    /// </summary>
    public static string? GetZipPath(string gamePath, IReadOnlySet<string> zipNames)
    {
        string path = gamePath.Replace('\\', '/').TrimStart('/');
        if (zipNames.Contains(path))
        {
            return path;
        }
        string prefixed = ZipNewFilesPrefix + path;
        return zipNames.Contains(prefixed) ? prefixed : null;
    }


    /// <summary>
    /// VFS 目录下的相对路径换成游戏目录下的
    /// </summary>
    public static string ToGamePath(GryphlinePatchInfo info, string vfsRelative)
    {
        string vfs = string.IsNullOrWhiteSpace(info.VfsBasePath) ? GryphlineDownloadPlanner.VfsDirectory : info.VfsBasePath.Replace('\\', '/').Trim('/');
        return vfs + "/" + vfsRelative.Replace('\\', '/').TrimStart('/');
    }


    /// <summary>
    /// delete_files.txt：每行一个相对游戏目录的路径
    /// </summary>
    public static IReadOnlyList<string> ParseDeleteList(string text)
    {
        return text.Split('\n')
                   .Select(x => x.Trim().TrimStart('﻿').Replace('\\', '/'))
                   .Where(x => x.Length > 0)
                   .ToList()
                   .AsReadOnly();
    }


    public static GryphlinePatchInfo ParsePatchInfo(ReadOnlySpan<byte> json)
    {
        return JsonSerializer.Deserialize(json, GryphlineLauncherJsonContext.Default.GryphlinePatchInfo)
               ?? throw new InvalidDataException("patch.json is empty.");
    }


    public static GryphlineVerifyFiles ParseVerifyFiles(ReadOnlySpan<byte> json)
    {
        return JsonSerializer.Deserialize(json, GryphlineLauncherJsonContext.Default.GryphlineVerifyFiles)
               ?? throw new InvalidDataException("verify_files.json is empty.");
    }


    public static IReadOnlySet<string> ParseJournal(string json)
    {
        try
        {
            string[]? list = JsonSerializer.Deserialize(json, GryphlineLauncherJsonContext.Default.StringArray);
            return new HashSet<string>(list ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }


    public static string SerializeJournal(IEnumerable<string> applied)
    {
        return JsonSerializer.Serialize(applied.ToArray(), GryphlineLauncherJsonContext.Default.StringArray);
    }


    public static string DecodeText(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);

}
