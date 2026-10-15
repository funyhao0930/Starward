using Microsoft.Extensions.Logging;
using Starward.Core.Games.Gryphline;
using Starward.Core.Launcher;
using Starward.Core.Launcher.Gryphline;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.RPC.GameInstall.Gryphline;

/// <summary>
/// 终末地的补丁更新与预下载。
/// <para/>
/// 补丁是一个按 1 GiB 切开的 WinZip AES zip，密码是接口给的 cd_key（见 <see cref="GryphlinePatch"/>）。
/// 预下载时只有分卷、没有 cd_key：先把分卷下载到暂存目录，正式上线后接口给出 cd_key，
/// 分卷是同一批文件（MD5 相同），只校验不重新下载，接着解密、打补丁。
/// <para/>
/// 本机版本记在加密的 config.ini 里，这里不解密它，判断方法见 <see cref="GryphlinePatchPlanner"/>。
/// <para/>
/// 打补丁的顺序：VFS 差分 → 从 zip 放入变了的文件 → 补齐本该没变却缺了的文件 → 删除旧文件 → 最后写 game_files 与 config.ini。
/// 差分打不上（本机旧文件被改过、缺了）的文件，改为从最新整包的 file_path 单独下载完整的新文件。
/// 每处理好一个文件记进暂存目录的进度文件，中断后继续时跳过。
/// </summary>
internal partial class GryphlineGameInstaller
{

    private sealed class GryphlinePatchPlan
    {
        public required string LocalVersion { get; init; }

        public required string TargetVersion { get; init; }

        public required string StagingDir { get; init; }

        public required IReadOnlyList<GryphlinePackageFile> Parts { get; init; }

        /// <summary>
        /// zip 的密码，预下载时为 null
        /// </summary>
        public string? CdKey { get; init; }

        /// <summary>
        /// 最新整包逐个文件下载的根目录，差分打不上时从这里下载完整的新文件；预下载时为 null
        /// </summary>
        public string? FilePath { get; init; }

        public IReadOnlyList<string> PartPaths => Parts.Select((x, i) => Path.Combine(StagingDir, GryphlinePatchPlanner.GetPartFileName(x.Url ?? "", i))).ToList();
    }



    #region Prepare



    /// <summary>
    /// 预下载：本机必须是最新版（game_files 与线上的一致），这时本机版本就是线上版本，拿它去问 pre_patch
    /// </summary>
    private async Task<GryphlinePatchPlan> PreparePredownloadAsync(GameInstallContext context, string gameDir, CancellationToken cancellationToken)
    {
        GryphlineLatestGame latest = await _launcherClient.GetLatestGameAsync(GryphlineLauncherClient.ENDFIELD_APP_CODE, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Endfield game package is not available.");
        string localVersion = latest.Version ?? throw new InvalidOperationException("Endfield game package has no version.");
        string? localMd5 = await GetLocalGameFilesMd5Async(gameDir, cancellationToken);
        if (!string.Equals(localMd5, latest.Package?.GameFilesMd5, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Endfield must be up to date before predownload.");
        }
        GryphlineLatestGame withLocal = await _launcherClient.GetLatestGameAsync(GryphlineLauncherClient.ENDFIELD_APP_CODE, localVersion, cancellationToken)
            ?? throw new InvalidOperationException("Endfield game package is not available.");
        GryphlinePatch prePatch = withLocal.PrePatch ?? throw new InvalidOperationException("Endfield has no predownload now.");
        string targetVersion = prePatch.Version ?? throw new InvalidOperationException("Endfield predownload has no version.");
        List<GryphlinePackageFile> parts = prePatch.Patches ?? [];
        if (parts.Count == 0 || parts.Any(x => string.IsNullOrWhiteSpace(x.Url) || x.Size <= 0))
        {
            throw new InvalidOperationException("Endfield predownload has no parts.");
        }
        context.LocalGameVersion = localVersion;
        context.LatestGameVersion = targetVersion;
        context.PredownloadVersion = targetVersion;
        context.DownloadMode = GameInstallDownloadMode.Patch;
        _logger.LogInformation("Prepare Endfield predownload: {local} -> {target}, {count} parts ({size} bytes).", localVersion, targetVersion, parts.Count, parts.Sum(x => x.Size));
        return new GryphlinePatchPlan
        {
            LocalVersion = localVersion,
            TargetVersion = targetVersion,
            StagingDir = GryphlinePatchPlanner.GetStagingDirectory(context.InstallPath, localVersion, targetVersion),
            Parts = parts,
        };
    }



    /// <summary>
    /// 更新：找出本机版本与对应的补丁。找不到时返回 null，改为按文件更新。
    /// <para/>
    /// 本机版本先看预下载留下的标记，再试 <see cref="GryphlinePatchPlanner.GetCandidateLocalVersions"/>；
    /// 有补丁的版本都要用明文 patch.json 比对本机文件确认，不能只信标记（之后可能用官方启动器更新过）。
    /// </summary>
    private async Task<GryphlinePatchPlan?> TryPrepareUpdateAsync(GameInstallContext context, string gameDir, CancellationToken cancellationToken)
    {
        try
        {
            GryphlineLatestGame latest = await _launcherClient.GetLatestGameAsync(GryphlineLauncherClient.ENDFIELD_APP_CODE, cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Endfield game package is not available.");
            string targetVersion = latest.Version ?? throw new InvalidOperationException("Endfield game package has no version.");
            string? filePath = latest.Package?.FilePath;

            var candidates = new List<string>();
            if (GryphlinePatchPlanner.FindMarker(context.InstallPath, targetVersion)?.LocalVersion is string marked)
            {
                candidates.Add(marked);
            }
            candidates.AddRange(GryphlinePatchPlanner.GetCandidateLocalVersions(targetVersion).Where(x => !candidates.Contains(x)));

            // 一起问，只留下有补丁、而且补丁通到最新版的
            GryphlineLatestGame?[] responses = await Task.WhenAll(candidates.Select(async version =>
            {
                try
                {
                    return await _launcherClient.GetLatestGameAsync(GryphlineLauncherClient.ENDFIELD_APP_CODE, version, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Get Endfield patch from {version}", version);
                    return null;
                }
            }));

            string vfsDir = ToFullPath(gameDir, GryphlineDownloadPlanner.VfsDirectory);
            for (int i = 0; i < candidates.Count; i++)
            {
                GryphlinePatch? patch = responses[i]?.Patch;
                if (patch is null || responses[i]!.Version != targetVersion || string.IsNullOrWhiteSpace(patch.CdKey)
                    || patch.Patches is not { Count: > 0 } parts || parts.Any(x => string.IsNullOrWhiteSpace(x.Url) || x.Size <= 0))
                {
                    continue;
                }
                GryphlinePatchInfo? info = await _polly.ExecuteAsync(async token => await _launcherClient.GetPatchInfoAsync(patch, token), cancellationToken);
                if (info is null)
                {
                    continue;
                }
                double score = GryphlinePatchPlanner.ScoreLocalMatch(info, relative => GetFileLength(vfsDir, relative));
                _logger.LogInformation("Endfield patch {local} -> {target}: {score:P1} of local files match.", candidates[i], targetVersion, score);
                if (score < GryphlinePatchPlanner.LocalMatchThreshold)
                {
                    continue;
                }
                context.LocalGameVersion = candidates[i];
                context.LatestGameVersion = targetVersion;
                context.DownloadMode = GameInstallDownloadMode.Patch;
                _logger.LogInformation("Prepare Endfield patch update: {local} -> {target}, {count} parts ({size} bytes).", candidates[i], targetVersion, parts.Count, parts.Sum(x => x.Size));
                return new GryphlinePatchPlan
                {
                    LocalVersion = candidates[i],
                    TargetVersion = targetVersion,
                    StagingDir = GryphlinePatchPlanner.GetStagingDirectory(context.InstallPath, candidates[i], targetVersion),
                    Parts = parts,
                    CdKey = patch.CdKey,
                    FilePath = filePath,
                };
            }
            _logger.LogInformation("No Endfield patch matches the local files, update file by file.");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Prepare Endfield patch update, update file by file instead.");
            return null;
        }
    }


    private static long? GetFileLength(string root, string relative)
    {
        try
        {
            var file = new FileInfo(ToFullPath(root, relative));
            return file.Exists ? file.Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }


    private static async Task<string?> GetLocalGameFilesMd5Async(string gameDir, CancellationToken cancellationToken)
    {
        string path = Path.Combine(gameDir, GryphlineVersionMapper.GameFilesName);
        if (!File.Exists(path))
        {
            return null;
        }
        await using FileStream fs = File.OpenRead(path);
        return Convert.ToHexStringLower(await MD5.HashDataAsync(fs, cancellationToken));
    }



    #endregion




    #region Download



    /// <summary>
    /// 下载分卷到暂存目录，每卷校验 MD5。已经下好的只校验，未下完的续传
    /// </summary>
    private async Task DownloadPatchPartsAsync(GameInstallContext context, GryphlinePatchPlan plan, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(plan.StagingDir);
        IReadOnlyList<string> paths = plan.PartPaths;
        context.Progress_DownloadTotalBytes = plan.Parts.Sum(x => x.Size);
        context.Progress_DownloadFinishBytes = 0;
        context.State = GameInstallState.Downloading;
        _logger.LogInformation("Endfield: download {count} patch parts ({size} bytes) to {dir}.", paths.Count, context.Progress_DownloadTotalBytes, plan.StagingDir);
        await Parallel.ForEachAsync(Enumerable.Range(0, paths.Count), cancellationToken, async (i, token) =>
        {
            GryphlinePackageFile part = plan.Parts[i];
            FileChecksum checksum = string.IsNullOrWhiteSpace(part.Md5) ? throw new InvalidDataException($"Patch part has no MD5: {part.Url}") : FileChecksum.Md5(part.Md5);
            await _polly.ExecuteAsync(async t => await _gameInstallHelper.DownloadToFileAsync(context, paths[i], part.Url!, part.Size, checksum, t), token);
        });
    }


    private async Task WritePredownloadMarkerAsync(GryphlinePatchPlan plan, CancellationToken cancellationToken)
    {
        var marker = new GryphlinePredownloadMarker { LocalVersion = plan.LocalVersion, TargetVersion = plan.TargetVersion };
        await File.WriteAllTextAsync(Path.Combine(plan.StagingDir, GryphlinePatchPlanner.PredownloadMarkerFileName), GryphlinePatchPlanner.SerializeMarker(marker), cancellationToken);
        _logger.LogInformation("Endfield: predownload {local} -> {target} finished.", plan.LocalVersion, plan.TargetVersion);
    }



    #endregion




    #region Apply



    private async Task UpdateByPatchAsync(GameInstallContext context, GryphlinePatchPlan plan, string gameDir, CancellationToken cancellationToken)
    {
        string cdKey = plan.CdKey ?? throw new InvalidOperationException("Endfield patch has no cd_key.");
        await DownloadPatchPartsAsync(context, plan, cancellationToken);

        foreach (string file in Directory.EnumerateFiles(gameDir, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        context.State = GameInstallState.Merging;
        context.Progress_Percent = 0;

        List<(string GamePath, long Size, string Md5)> fallback = await Task.Run(() => ApplyPatch(context, plan, cdKey, gameDir, cancellationToken), cancellationToken);

        if (fallback.Count > 0)
        {
            await DownloadFallbackAsync(context, plan, gameDir, fallback, cancellationToken);
        }
        await Task.Run(() => FinishPatch(context, plan, cdKey, gameDir, cancellationToken), cancellationToken);
        DeleteStagingRoot(context.InstallPath);
        _logger.LogInformation("Endfield: patch update {local} -> {target} finished.", plan.LocalVersion, plan.TargetVersion);
    }



    /// <summary>
    /// 打差分、放入变了的文件、检查没变的文件。返回要改为下载完整新文件的那些
    /// </summary>
    private List<(string GamePath, long Size, string Md5)> ApplyPatch(GameInstallContext context, GryphlinePatchPlan plan, string cdKey, string gameDir, CancellationToken cancellationToken)
    {
        using PatchArchive archive = PatchArchive.Open(plan.PartPaths, cdKey);
        GryphlinePatchInfo info = GryphlinePatchPlanner.ParsePatchInfo(archive.ReadAllBytes(GryphlinePatchPlanner.PatchInfoName));
        GryphlineVerifyFiles verify = GryphlinePatchPlanner.ParseVerifyFiles(archive.ReadAllBytes(GryphlinePatchPlanner.VerifyFilesName));
        var manifestNames = new HashSet<string>(GryphlineDownloadPlanner.ManifestFiles, StringComparer.OrdinalIgnoreCase);
        List<GryphlinePatchFile> diffs = info.Files?.Where(x => x.Patch is { Count: > 0 }).ToList() ?? [];
        List<GryphlineVerifyFile> moves = verify.Move?.Where(x => !manifestNames.Contains(x.Path)).ToList() ?? [];
        List<GryphlinePatchFile> unchanged = info.Files?.Where(x => x.Patch is not { Count: > 0 } && string.IsNullOrWhiteSpace(x.LocalPath)).ToList() ?? [];

        string journalPath = Path.Combine(plan.StagingDir, GryphlinePatchPlanner.JournalFileName);
        var applied = new HashSet<string>(File.Exists(journalPath) ? GryphlinePatchPlanner.ParseJournal(File.ReadAllText(journalPath)) : [], StringComparer.OrdinalIgnoreCase);
        void MarkApplied(string gamePath)
        {
            applied.Add(gamePath);
            File.WriteAllText(journalPath, GryphlinePatchPlanner.SerializeJournal(applied));
        }

        var fallback = new List<(string, long, string)>();
        double totalBytes = Math.Max(1, diffs.Sum(x => x.Size) + moves.Sum(x => x.Size));
        double doneBytes = 0;

        foreach (GryphlinePatchFile file in diffs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string gamePath = GryphlinePatchPlanner.ToGamePath(info, file.Name);
            if (!applied.Contains(gamePath))
            {
                if (TryApplyDiff(archive, info, file, gameDir, gamePath, cancellationToken))
                {
                    MarkApplied(gamePath);
                }
                else
                {
                    fallback.Add((gamePath, file.Size, file.Md5 ?? ""));
                }
            }
            doneBytes += file.Size;
            context.Progress_Percent = Math.Min(1, doneBytes / totalBytes);
        }

        foreach (GryphlineVerifyFile file in moves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!applied.Contains(file.Path))
            {
                string zipPath = GryphlinePatchPlanner.GetZipPath(file.Path, archive.Names)
                    ?? throw new InvalidDataException($"File is missing from the Endfield patch: {file.Path}");
                ExtractEntry(archive, zipPath, ToFullPath(gameDir, file.Path), file.Md5, cancellationToken);
                MarkApplied(file.Path);
            }
            doneBytes += file.Size;
            context.Progress_Percent = Math.Min(1, doneBytes / totalBytes);
        }

        // 没变的文件本机该有；缺了或大小不对的（之前被删、被改）同样下载
        foreach (GryphlinePatchFile file in unchanged)
        {
            string gamePath = GryphlinePatchPlanner.ToGamePath(info, file.Name);
            if (GetFileLength(gameDir, gamePath) != file.Size)
            {
                fallback.Add((gamePath, file.Size, file.Md5 ?? ""));
            }
        }
        _logger.LogInformation("Endfield: applied {diffs} diffs and {moves} files, {fallback} files need a full download.", diffs.Count, moves.Count, fallback.Count);
        return fallback;
    }



    /// <summary>
    /// 打一个差分。旧文件不在、大小不对、或打出来的 MD5 不对时返回 false，游戏目录里不会留下打坏的文件
    /// </summary>
    private bool TryApplyDiff(PatchArchive archive, GryphlinePatchInfo info, GryphlinePatchFile file, string gameDir, string gamePath, CancellationToken cancellationToken)
    {
        GryphlinePatchDiff diff = file.Patch![0];
        string basePath = ToFullPath(gameDir, GryphlinePatchPlanner.ToGamePath(info, diff.BaseFile));
        string target = ToFullPath(gameDir, gamePath);
        string temp = target + "_tmp";
        if (!File.Exists(basePath) || new FileInfo(basePath).Length != diff.BaseSize)
        {
            _logger.LogWarning("Endfield: base file of {file} is missing or changed, download it instead.", gamePath);
            return false;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var oldData = new FileStream(basePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess))
            using (Stream diffData = archive.Open(GryphlinePatchPlanner.ZipDiffPrefix + diff.Patch))
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            using (var hashing = new HashingWriteStream(output))
            {
                HDiffSingleStream.Patch(oldData, diffData, hashing, cancellationToken);
                if (!string.Equals(hashing.GetMd5(), file.Md5, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("MD5 mismatch.");
                }
            }
            File.Move(temp, target, true);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Endfield: apply diff of {file} failed, download it instead.", gamePath);
            File.Delete(temp);
            return false;
        }
    }



    /// <summary>
    /// 从 zip 取出一个文件放进游戏目录，先写到 _tmp，MD5 对了才换上去
    /// </summary>
    private static void ExtractEntry(PatchArchive archive, string zipPath, string target, string? md5, CancellationToken cancellationToken)
    {
        string temp = target + "_tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using (Stream source = archive.Open(zipPath))
        using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        using (var hashing = new HashingWriteStream(output))
        {
            byte[] buffer = new byte[1 << 16];
            int read;
            while ((read = source.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hashing.Write(buffer, 0, read);
            }
            if (!string.IsNullOrWhiteSpace(md5) && !string.Equals(hashing.GetMd5(), md5, StringComparison.OrdinalIgnoreCase))
            {
                output.Close();
                File.Delete(temp);
                throw new InvalidDataException($"MD5 mismatch in the Endfield patch: {zipPath}");
            }
        }
        File.Move(temp, target, true);
    }



    /// <summary>
    /// 差分打不上、或本机缺了的文件，从最新整包单独下载
    /// </summary>
    private async Task DownloadFallbackAsync(GameInstallContext context, GryphlinePatchPlan plan, string gameDir, List<(string GamePath, long Size, string Md5)> files, CancellationToken cancellationToken)
    {
        string filePath = plan.FilePath ?? throw new InvalidOperationException("Endfield game package has no file path.");
        context.Progress_DownloadTotalBytes = files.Sum(x => x.Size);
        context.Progress_DownloadFinishBytes = 0;
        context.State = GameInstallState.Downloading;
        _logger.LogInformation("Endfield: download {count} files ({size} bytes) that the patch could not produce.", files.Count, context.Progress_DownloadTotalBytes);
        await Parallel.ForEachAsync(files, cancellationToken, async (file, token) =>
        {
            string path = ToFullPath(gameDir, file.GamePath);
            string url = GryphlineDownloadPlanner.GetFileUrl(filePath, file.GamePath);
            await _polly.ExecuteAsync(async t => await _gameInstallHelper.DownloadToFileAsync(context, path, url, file.Size, FileChecksum.Md5(file.Md5), t), token);
        });
        context.State = GameInstallState.Merging;
    }



    /// <summary>
    /// 删掉旧文件，最后写 game_files 与 config.ini：官方启动器与 Starward 都靠 game_files 判断是不是最新版
    /// </summary>
    private void FinishPatch(GameInstallContext context, GryphlinePatchPlan plan, string cdKey, string gameDir, CancellationToken cancellationToken)
    {
        using PatchArchive archive = PatchArchive.Open(plan.PartPaths, cdKey);
        GryphlinePatchInfo info = GryphlinePatchPlanner.ParsePatchInfo(archive.ReadAllBytes(GryphlinePatchPlanner.PatchInfoName));
        GryphlineVerifyFiles verify = GryphlinePatchPlanner.ParseVerifyFiles(archive.ReadAllBytes(GryphlinePatchPlanner.VerifyFilesName));
        IReadOnlyList<string> deleteList = archive.Names.Contains(GryphlinePatchPlanner.DeleteFilesName)
                                         ? GryphlinePatchPlanner.ParseDeleteList(GryphlinePatchPlanner.DecodeText(archive.ReadAllBytes(GryphlinePatchPlanner.DeleteFilesName)))
                                         : [];
        // 删除清单与新文件不会重叠，以防万一还是跳过新版本有的文件
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        keep.UnionWith(info.Files?.Select(x => GryphlinePatchPlanner.ToGamePath(info, x.Name)) ?? []);
        keep.UnionWith(verify.Move?.Select(x => x.Path) ?? []);
        int deleted = 0;
        foreach (string relative in deleteList)
        {
            if (keep.Contains(relative))
            {
                continue;
            }
            string path = ToFullPath(gameDir, relative);
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                deleted++;
            }
        }
        _logger.LogInformation("Endfield: deleted {count} old files.", deleted);

        foreach (string name in GryphlineDownloadPlanner.ManifestFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GryphlineVerifyFile? file = verify.Move?.FirstOrDefault(x => string.Equals(x.Path, name, StringComparison.OrdinalIgnoreCase));
            string? zipPath = GryphlinePatchPlanner.GetZipPath(name, archive.Names);
            if (zipPath is null)
            {
                throw new InvalidDataException($"File is missing from the Endfield patch: {name}");
            }
            ExtractEntry(archive, zipPath, ToFullPath(gameDir, name), file?.Md5, cancellationToken);
        }
        context.Progress_Percent = 1;
    }



    private void DeleteStagingRoot(string installPath)
    {
        string root = Path.Combine(installPath, GryphlinePatchPlanner.StagingFolderName);
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Delete staging directory {dir}", root);
        }
    }



    /// <summary>
    /// 打开分卷接起来的补丁 zip，读中央目录。条目逐个顺序打开，不能多线程同时用
    /// </summary>
    private sealed class PatchArchive : IDisposable
    {
        private readonly MultiFileStream _stream;

        private readonly Dictionary<string, ZipEntryInfo> _entries;

        private readonly string _password;

        public IReadOnlySet<string> Names { get; }

        private PatchArchive(MultiFileStream stream, Dictionary<string, ZipEntryInfo> entries, string password)
        {
            _stream = stream;
            _entries = entries;
            _password = password;
            Names = new HashSet<string>(entries.Keys, StringComparer.OrdinalIgnoreCase);
        }

        public static PatchArchive Open(IReadOnlyList<string> parts, string password)
        {
            MultiFileStream stream = MultiFileStream.OpenRead(parts);
            try
            {
                long tailLength = Math.Min(ZipCentralDirectory.MaxTailSize, stream.Length);
                long tailStart = stream.Length - tailLength;
                byte[] tail = new byte[tailLength];
                stream.Position = tailStart;
                stream.ReadExactly(tail);
                ZipCentralDirectoryInfo info = ZipCentralDirectory.Locate(tail, tailStart);
                byte[] centralDirectory = new byte[info.Size];
                stream.Position = info.Offset;
                stream.ReadExactly(centralDirectory);
                var entries = ZipCentralDirectory.ReadEntries(centralDirectory)
                                                 .Where(x => !x.IsDirectory)
                                                 .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
                return new PatchArchive(stream, entries, password);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        public Stream Open(string name)
        {
            ZipEntryInfo entry = _entries.TryGetValue(name, out ZipEntryInfo? e) ? e : throw new FileNotFoundException($"Not in the Endfield patch: {name}");
            return ZipEntryReader.Open(_stream, entry, _password);
        }

        public byte[] ReadAllBytes(string name)
        {
            using Stream stream = Open(name);
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        public void Dispose() => _stream.Dispose();
    }



    /// <summary>
    /// 边写边算 MD5
    /// </summary>
    private sealed class HashingWriteStream(Stream inner) : Stream
    {
        private readonly IncrementalHash _md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        public string GetMd5() => Convert.ToHexStringLower(_md5.GetHashAndReset());

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _md5.AppendData(buffer);
            inner.Write(buffer);
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _md5.Dispose();
            }
            base.Dispose(disposing);
        }
    }



    #endregion

}
