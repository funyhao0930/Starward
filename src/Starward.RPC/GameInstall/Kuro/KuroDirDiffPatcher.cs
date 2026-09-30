using Microsoft.Extensions.Logging;
using Snap.HPatch;
using Starward.Core.Launcher.Kuro;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp;

namespace Starward.RPC.GameInstall.Kuro;

/// <summary>
/// 应用鸣潮的 <c>.krpdiff</c>（HDiffPatch 目录差分）。
/// <para/>
/// 官方启动器是把 hpatchz.exe 当子进程跑；这里只解析目录差分的外壳
/// （见 <see cref="HDiffDirDiffHeader"/>），里面那份单文件差分交给 Starward 本来就有的
/// hpatch（米哈游的 ldiff 也是它在打），旧文件与新文件都用 <see cref="MultiFileStream"/> 接起来。
/// <para/>
/// 新文件先写进暂存目录，校验过清单上的 MD5 才移进游戏目录：
/// 同一组里的旧文件在打补丁时还要读，不能边读边覆盖。
/// </summary>
internal class KuroDirDiffPatcher
{

    private readonly ILogger<KuroDirDiffPatcher> _logger;

    private readonly GameInstallHelper _gameInstallHelper;


    public KuroDirDiffPatcher(ILogger<KuroDirDiffPatcher> logger, GameInstallHelper gameInstallHelper)
    {
        _logger = logger;
        _gameInstallHelper = gameInstallHelper;
    }



    /// <summary>
    /// 读差分包的文件头
    /// </summary>
    public static HDiffDirDiffHeader ReadHeader(string diffPath)
    {
        using FileStream fs = File.OpenRead(diffPath);
        return HDiffDirDiffHeader.Parse(fs, Decompress);
    }


    private static byte[] Decompress(byte[] data, int size)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(data, size).ToArray();
    }



    /// <summary>
    /// 把一组旧文件打成新文件，放进游戏目录。
    /// </summary>
    /// <param name="context">用来累计读写的字节数</param>
    /// <param name="gameDir">游戏目录，旧文件从这里读，新文件最后放回这里</param>
    /// <param name="diffPath">已下载并校验过的差分包</param>
    /// <param name="group">清单上这个差分包的说明，用来校验结果</param>
    /// <param name="workDir">暂存新文件的目录，用完即删</param>
    /// <exception cref="InvalidDataException">差分包与本机文件对不上，或打出来的结果校验不过</exception>
    /// <exception cref="NotSupportedException">差分包的格式认不出来</exception>
    public async Task ApplyAsync(GameInstallContext context, string gameDir, string diffPath, KuroResourceGroup group, string workDir, CancellationToken cancellationToken = default)
    {
        HDiffDirDiffHeader header = ReadHeader(diffPath);
        if (!header.OldPathIsDir || !header.NewPathIsDir)
        {
            throw new NotSupportedException("Only directory to directory diffs are supported.");
        }

        IReadOnlyList<string> oldRefPaths = header.GetOldRefPaths();
        IReadOnlyList<(string Path, long Size)> newRefFiles = header.GetNewRefFiles();
        CheckOldFiles(gameDir, oldRefPaths, header.OldRefSizes);

        if (Directory.Exists(workDir))
        {
            Directory.Delete(workDir, true);
        }
        Directory.CreateDirectory(workDir);

        cancellationToken.ThrowIfCancellationRequested();
        // hpatch 是同步的原生调用，中途不能取消，只能放到线程池上等它做完。
        // 最大的一组是 6 GB 的 pakchunk0，要跑一两分钟。
        bool success = await Task.Run(() =>
        {
            MultiFileStream? oldStream = oldRefPaths.Count > 0 ? MultiFileStream.OpenRead(oldRefPaths.Select(x => ToFullPath(gameDir, x)).ToList()) : null;
            try
            {
                using var diffStream = new FileSliceStream(diffPath, header.HDiffDataOffset, header.HDiffDataSize);
                using MultiFileStream newStream = MultiFileStream.Create(newRefFiles.Select(x => ToFullPath(workDir, x.Path)).ToList(), newRefFiles.Select(x => x.Size).ToList());
                // 不用 Snap.HPatch 自带的外壳：压缩段超过 2 GiB 时它会读不出数据（见 HPatchLarge）
                return HPatchLarge.PatchZstandard(oldStream, diffStream, newStream);
            }
            finally
            {
                oldStream?.Dispose();
            }
        }, CancellationToken.None);
        if (!success)
        {
            throw new InvalidDataException($"Patch failed: {Path.GetFileName(diffPath)}");
        }
        Interlocked.Add(ref context.storageReadBytes, header.OldRefSize);
        Interlocked.Add(ref context.storageWriteBytes, header.NewRefSize);

        // 内容完全相同的文件不在差分数据里，直接从旧目录复制
        foreach ((string newPath, string oldPath) in header.GetSameFileCopies())
        {
            string target = ToFullPath(workDir, newPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(ToFullPath(gameDir, oldPath), target, true);
        }
        foreach (string path in header.GetNewEmptyFiles())
        {
            string target = ToFullPath(workDir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, [], cancellationToken);
        }

        // 以清单为准校验：差分包自带的 fadler64 只能证明打补丁的过程没出错，
        // 清单上的 MD5 才能证明结果就是官方要的文件
        foreach (KuroResourceFile dst in group.DstFiles)
        {
            string path = ToFullPath(workDir, dst.Dest);
            if (!await _gameInstallHelper.CheckFileMD5Async(context, path, dst.Size, dst.Md5, cancellationToken))
            {
                throw new InvalidDataException($"Patched file does not match the manifest: {dst.Dest}");
            }
        }

        // 全部校验通过才动游戏目录
        var dstSet = new HashSet<string>(group.DstFiles.Select(x => Normalize(x.Dest)), StringComparer.OrdinalIgnoreCase);
        foreach (KuroResourceFile dst in group.DstFiles)
        {
            string source = ToFullPath(workDir, dst.Dest);
            string target = ToFullPath(gameDir, dst.Dest);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                File.SetAttributes(target, FileAttributes.Normal);
            }
            File.Move(source, target, true);
        }
        // 旧文件被改名或拆开时，清单上不再有的旧文件要删掉，官方启动器也这样做（RemoveSourceFiles）
        foreach (KuroResourceFile src in group.SrcFiles)
        {
            if (!dstSet.Contains(Normalize(src.Dest)))
            {
                string path = ToFullPath(gameDir, src.Dest);
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }
        }
        Directory.Delete(workDir, true);
        _logger.LogInformation("Applied {diff}: {old} -> {new} files", Path.GetFileName(diffPath), oldRefPaths.Count, group.DstFiles.Count);
    }



    /// <summary>
    /// 旧文件缺了或大小不对就不用打了，一定打不出对的结果
    /// </summary>
    private static void CheckOldFiles(string gameDir, IReadOnlyList<string> oldRefPaths, IReadOnlyList<long>? oldRefSizes)
    {
        for (int i = 0; i < oldRefPaths.Count; i++)
        {
            string path = ToFullPath(gameDir, oldRefPaths[i]);
            if (!File.Exists(path))
            {
                throw new InvalidDataException($"Source file not found: {oldRefPaths[i]}");
            }
            if (oldRefSizes is not null && new FileInfo(path).Length != oldRefSizes[i])
            {
                throw new InvalidDataException($"Source file size does not match: {oldRefPaths[i]}");
            }
        }
    }


    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');


    /// <summary>
    /// 清单与差分包里的路径都是相对的正斜杠路径，拼完要确认没有跑出目录外
    /// </summary>
    internal static string ToFullPath(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, Normalize(relative)));
        string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) && !string.Equals(full + Path.DirectorySeparatorChar, rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Path escapes the target directory: {relative}");
        }
        return full;
    }

}
