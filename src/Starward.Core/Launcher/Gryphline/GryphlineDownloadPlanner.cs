namespace Starward.Core.Launcher.Gryphline;

/// <summary>
/// 把终末地的整包信息整理成逐个文件的下载计划。
/// <para/>
/// 官方启动器是下载整包分卷再解压，要先腾出分卷加解压后两份的空间（约 119 GB）；
/// 同一份内容也能按文件从 <see cref="GryphlineGamePackage.FilePath"/> 逐个下载，
/// 多下约 9%，但不必解压、磁盘只需一份、每个文件都能续传与校验。
/// 文件清单取自整包末尾的 zip 中央目录（明文，带 CRC32），不需要解密本机加密的 game_files。
/// <para/>
/// 全是纯计算，不碰网络与磁盘。
/// </summary>
public static class GryphlineDownloadPlanner
{

    /// <summary>
    /// 游戏自带的 VFS 资源目录。更新后旧版本的 .chk / .blc 会改名留在这里，
    /// 官方的补丁用 delete_files.txt 删掉它们；按文件更新没有那份清单，改为删掉清单上没有的文件
    /// </summary>
    public const string VfsDirectory = "Endfield_Data/StreamingAssets/VFS";


    /// <summary>
    /// 最后才写的文件：官方启动器与 Starward 都靠 game_files 的 MD5 判断是不是最新版，
    /// 中途断掉时不能先有新的 game_files，否则半装好的游戏会被当成已是最新
    /// </summary>
    public static IReadOnlyList<string> ManifestFiles { get; } = [GryphlineVersionMapper.GameFilesName, "config.ini"];



    /// <summary>
    /// 一个文件的下载地址，路径逐段转义，目录分隔符保留
    /// </summary>
    public static string GetFileUrl(string filePath, string relative)
    {
        string escaped = string.Join('/', relative.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
        return filePath.TrimEnd('/') + "/" + escaped;
    }


    /// <summary>
    /// 整包里要下载的文件：去掉目录，<see cref="ManifestFiles"/> 排到最后
    /// </summary>
    public static IReadOnlyList<ZipEntryInfo> GetFiles(IEnumerable<ZipEntryInfo> entries)
    {
        var manifest = new HashSet<string>(ManifestFiles, StringComparer.OrdinalIgnoreCase);
        List<ZipEntryInfo> files = entries.Where(x => !x.IsDirectory).ToList();
        return files.Where(x => !manifest.Contains(x.Name))
                    .Concat(ManifestFiles.Select(m => files.FirstOrDefault(x => string.Equals(x.Name, m, StringComparison.OrdinalIgnoreCase))).OfType<ZipEntryInfo>())
                    .ToList()
                    .AsReadOnly();
    }


    /// <summary>
    /// 把整包里的一段（各分卷首尾相接之后的偏移）换成各分卷自己的范围
    /// </summary>
    /// <returns>(分卷下标, 分卷内偏移, 长度)</returns>
    public static IReadOnlyList<(int Index, long Offset, long Length)> MapRange(IReadOnlyList<long> packSizes, long start, long length)
    {
        var list = new List<(int, long, long)>();
        long packStart = 0;
        long end = start + length;
        for (int i = 0; i < packSizes.Count && start < end; i++)
        {
            long packEnd = packStart + packSizes[i];
            if (start < packEnd && end > packStart)
            {
                long from = Math.Max(start, packStart);
                long to = Math.Min(end, packEnd);
                list.Add((i, from - packStart, to - from));
            }
            packStart = packEnd;
        }
        if (list.Sum(x => x.Item3) != length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "The range is outside the package.");
        }
        return list.AsReadOnly();
    }


    /// <summary>
    /// 更新或修复之后要删的旧文件：VFS 目录里、新清单上没有的文件。
    /// </summary>
    /// <param name="localFiles">VFS 目录下本机现有的文件，相对于游戏目录，正斜杠</param>
    /// <param name="files">新版本的文件</param>
    public static IReadOnlyList<string> GetStaleVfsFiles(IEnumerable<string> localFiles, IEnumerable<ZipEntryInfo> files)
    {
        var known = new HashSet<string>(files.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
        string prefix = VfsDirectory + "/";
        return localFiles.Select(x => x.Replace('\\', '/'))
                         .Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !known.Contains(x))
                         .ToList()
                         .AsReadOnly();
    }

}
