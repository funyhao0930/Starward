using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Polly;
using Polly.Retry;
using SharpSevenZip;
using Snap.HPatch;
using Starward.Core;
using Starward.RPC.GameInstall;
using Starward.Setup.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Update;

internal class UpdateService
{

    private readonly ILogger<UpdateService> _logger;

    private readonly HttpClient _httpClient;

    private readonly ReleaseClient _releaseClient;

    private readonly ResiliencePipeline _polly;


    public UpdateService(ILogger<UpdateService> logger, HttpClient httpClient, ReleaseClient releaseClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _releaseClient = releaseClient;
        _polly = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 5,
            BackoffType = DelayBackoffType.Linear
        }).Build();
    }



    private string _targetPath;

    private string _updateCacheFolder;

    private ReleaseManifest _releaseManifest;

    private ConcurrentDictionary<string, string> _currentVersionFilesHash;



    public static bool UpdateFinished { get; private set; }

    public UpdateState State { get; private set; }

    public long Progress_TotalBytes { get; private set; }

    private long _progress_DownloadBytes;
    public long Progress_DownloadBytes => _progress_DownloadBytes;

    public string? ErrorMessage { get; private set; }



    private bool _isUpdating;

    private CancellationTokenSource? _cancellationTokenSource;



    public async Task<ReleaseInfoDetail?> CheckUpdateAsync(bool disableIgnore = false)
    {
        _ = NuGetVersion.TryParse(AppConfig.AppVersion, out var currentVersion);
        _ = NuGetVersion.TryParse(AppConfig.IgnoreVersion, out var ignoreVersion);
#if DEBUG
        var release = await _releaseClient.GetLatestGithubReleaseInfoDetailAsync(AppConfig.EnablePreviewRelease, RuntimeInformation.ProcessArchitecture, InstallType.Portable);
#else
        var release = await _releaseClient.GetLatestGithubReleaseInfoDetailAsync(AppConfig.EnablePreviewRelease, RuntimeInformation.ProcessArchitecture, AppConfig.InstallType);
#endif
        _logger.LogInformation("Current version: {currentVersion}, latest version: {latestVersion}, ignore version: {ignoreVersion}.", AppConfig.AppVersion, release?.Version, ignoreVersion);
        _ = NuGetVersion.TryParse(release?.Version, out var newVersion);
        if (newVersion! > currentVersion!)
        {
            if (disableIgnore || newVersion! > ignoreVersion!)
            {
                return release;
            }
        }
        return null;
    }



    public async Task<ReleaseInfoDetail> GetLatestVersionAsync(CancellationToken cancellation = default)
    {
#if DEBUG
        return await _releaseClient.GetLatestGithubReleaseInfoDetailAsync(AppConfig.EnablePreviewRelease, RuntimeInformation.ProcessArchitecture, InstallType.Portable, cancellation);
#else
        return await _releaseClient.GetLatestGithubReleaseInfoDetailAsync(AppConfig.EnablePreviewRelease, RuntimeInformation.ProcessArchitecture, AppConfig.InstallType, cancellation);
#endif
    }



    public async Task StartUpdateAsync(ReleaseInfoDetail release)
    {
        if (_isUpdating || UpdateFinished)
        {
            State = UpdateFinished ? UpdateState.Finish : State;
            return;
        }
        try
        {
            ClearState();
            _isUpdating = true;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            State = UpdateState.Pending;
            if (!AppConfig.IsPortable)
            {
                // 无法自动更新
                ErrorMessage = Lang.UpdateService_CannotUpdateAutomatically;
                State = UpdateState.NotSupport;
                return;
            }
            if (string.IsNullOrWhiteSpace(release.ManifestUrl))
            {
                await UpdateFromPackageAsync(release, _cancellationTokenSource.Token);
            }
            else
            {
                await StartInternalAsync(release, _cancellationTokenSource.Token);
            }
            if (State is UpdateState.Finish)
            {
                UpdateFinished = true;
            }
            else if (State is not UpdateState.Finish and not UpdateState.Error)
            {
                _logger.LogWarning("Update stopped with unexpected state: {state}", State);
                State = UpdateState.Stop;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start update");
            State = UpdateState.Error;
            ErrorMessage = ex.Message;
        }
        finally
        {
            _isUpdating = false;
        }
    }



    private async Task StartInternalAsync(ReleaseInfoDetail release, CancellationToken cancellationToken = default)
    {
        try
        {
            string manifestUrl = release.ManifestUrl;
            if (release.Diffs?.TryGetValue(AppConfig.AppVersion, out var diff) ?? false)
            {
                manifestUrl = diff.ManifestUrl;
            }
            var manifest = await _releaseClient.GetReleaseManifestAsync(manifestUrl, cancellationToken);
            string targetPath = Path.GetDirectoryName(AppConfig.StarwardPortableLauncherExecutePath)!;
            await PrepareForUpdateAsync(manifest, targetPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start update internal");
            State = UpdateState.Error;
            ErrorMessage = ex.Message;
        }
    }



    public void StopUpdate()
    {
        _cancellationTokenSource?.Cancel();
    }



    private void ClearState()
    {
        State = UpdateState.Stop;
        Progress_TotalBytes = 0;
        _progress_DownloadBytes = 0;
        ErrorMessage = null;
    }



    #region Package



    /// <summary>
    /// GitHub Release 沒有逐檔 manifest，整包下載可攜版 7z，解出新的 app-{version} 後改寫 version.ini
    /// </summary>
    private async Task UpdateFromPackageAsync(ReleaseInfoDetail release, CancellationToken cancellationToken = default)
    {
        _targetPath = Path.GetDirectoryName(AppConfig.StarwardPortableLauncherExecutePath)!;
        _updateCacheFolder = Path.Combine(AppConfig.CacheFolder, "update");
        string packagePath = Path.Combine(_updateCacheFolder, Path.GetFileName(new Uri(release.PackageUrl).LocalPath));
        // 解壓在安裝目錄底下，和目標同一個磁碟，新版資料夾才能直接搬過去
        string extractPath = Path.Combine(_targetPath, ".update");
        try
        {
            _logger.LogInformation("Update Starward from package {url}", release.PackageUrl);
            Directory.CreateDirectory(_updateCacheFolder);
            State = UpdateState.Downloading;
            Progress_TotalBytes = release.PackageSize;
            await _polly.ExecuteAsync(async (pollyToken) => await DownloadPackageAsync(release, packagePath, pollyToken), cancellationToken);

            State = UpdateState.Pending;
            TryDeleteDirectory(extractPath);
            await ExtractPackageAsync(packagePath, extractPath, cancellationToken);

            // 封包的根目錄是 Starward\，裡面有 app-{version}、Starward.exe、version.ini
            string root = Path.Combine(extractPath, "Starward");
            string[] appFolders = Directory.Exists(root) ? Directory.GetDirectories(root, "app-*") : [];
            if (appFolders.Length != 1 || !File.Exists(Path.Combine(appFolders[0], "Starward.exe")))
            {
                throw new InvalidDataException($"Unexpected package layout: {release.PackageUrl}");
            }
            string targetApp = Path.Combine(_targetPath, Path.GetFileName(appFolders[0]));
            TryDeleteDirectory(targetApp);
            Directory.Move(appFolders[0], targetApp);

            // 啟動器只讀 version.ini 決定開哪個 app-*，被占用時沿用舊的也能正常啟動
            try
            {
                string launcher = Path.Combine(root, "Starward.exe");
                if (File.Exists(launcher))
                {
                    File.Copy(launcher, Path.Combine(_targetPath, "Starward.exe"), true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Replace launcher");
            }

            // 最後才換 version.ini，前面任何一步失敗，啟動器都還會開舊版
            string versionIni = Path.Combine(root, "version.ini");
            if (File.Exists(versionIni))
            {
                File.Copy(versionIni, Path.Combine(_targetPath, "version.ini"), true);
            }
            else
            {
                await File.WriteAllTextAsync(Path.Combine(_targetPath, "version.ini"), $"version={release.Version}", CancellationToken.None);
            }

            TryDeleteDirectory(extractPath);
            DeleteUpdateCacheFolder();
            State = UpdateState.Finish;
            _logger.LogInformation("Update Starward finished");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Update Starward canceled");
            State = UpdateState.Stop;
            TryDeleteDirectory(extractPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update Starward from package failed");
            State = UpdateState.Error;
            ErrorMessage = ex.Message;
            TryDeleteDirectory(extractPath);
        }
    }



    private async Task DownloadPackageAsync(ReleaseInfoDetail release, string path, CancellationToken cancellationToken = default)
    {
        using var fs = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > release.PackageSize)
        {
            fs.SetLength(0);
        }
        Interlocked.Exchange(ref _progress_DownloadBytes, fs.Length);
        if (fs.Length < release.PackageSize)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, release.PackageUrl) { VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
                request.Headers.Range = new RangeHeaderValue(fs.Length, null);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                // 伺服器不支援續傳時會回整個檔案
                fs.Position = response.Content.Headers.ContentRange?.From ?? 0;
                fs.SetLength(fs.Position);
                Interlocked.Exchange(ref _progress_DownloadBytes, fs.Position);
                // 連線無聲斷掉時讀取會一直卡住，逾時後交給 Polly 重試，從已寫入的長度續傳
                using var hs = new IdleTimeoutStream(await response.Content.ReadAsStreamAsync(cancellationToken));
                var buffer = new byte[1 << 16];
                int length;
                while ((length = await hs.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, length), cancellationToken);
                    Interlocked.Add(ref _progress_DownloadBytes, length);
                }
                await fs.FlushAsync(cancellationToken);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                // HttpClient.Timeout 到期丟的是 TaskCanceledException，Polly 預設不重試取消，外層也會當成使用者取消
                throw new TimeoutException(ex.Message, ex);
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning("Download stalled at {position} bytes: {url} ({message})", fs.Length, release.PackageUrl, ex.Message);
                throw;
            }
        }

        fs.Position = 0;
        var sha256 = await SHA256.HashDataAsync(fs, cancellationToken);
        if (string.Equals(Convert.ToHexString(sha256), release.PackageHash, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _logger.LogWarning("Checksum failed: {path}", path);
        fs.SetLength(0);
        Interlocked.Exchange(ref _progress_DownloadBytes, 0);
        throw new Exception($"Checksum failed: {path}");
    }



    private static async Task ExtractPackageAsync(string packagePath, string extractPath, CancellationToken cancellationToken = default)
    {
        using var fs = File.OpenRead(packagePath);
        try
        {
            // 7-Zip 的 COM 物件沒有跨 apartment 的封送代理，在 UI 執行緒（STA）建立、到執行緒池（MTA）使用
            // 會 QueryInterface 失敗（E_NOINTERFACE），所以建立與解壓都要在同一條背景執行緒上
            Task extractTask = Task.Run(() =>
            {
                using var archive = new SharpSevenZipExtractor(fs, leaveOpen: true);
                archive.ExtractArchive(extractPath);
            }, cancellationToken);
            while (!extractTask.IsCompleted)
            {
                await Task.Delay(50, CancellationToken.None);
                if (cancellationToken.IsCancellationRequested)
                {
                    // 解壓本身不能取消，關掉來源串流讓它中止
                    fs.Dispose();
                }
            }
            await extractTask;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Extract operation canceled.", cancellationToken);
        }
    }



    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Delete directory {path}", path);
        }
    }



    #endregion



    #region Prepare



    private async Task PrepareForUpdateAsync(ReleaseManifest manifest, string targetPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Prepare for update starward");
            State = UpdateState.Pending;
            _targetPath = targetPath;
            _releaseManifest = manifest;
            _updateCacheFolder = Path.Combine(AppConfig.CacheFolder, "update");
            Directory.CreateDirectory(_updateCacheFolder);
            await GetCurrentVersionFilesHashAsync(cancellationToken);
            await UpdateAsync(cancellationToken);
            _logger.LogInformation("Update finished");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Prepare for update");
            State = UpdateState.Stop;
        }
    }



    private async Task GetCurrentVersionFilesHashAsync(CancellationToken cancellationToken = default)
    {
        if (_currentVersionFilesHash is null)
        {
            var files = Directory.GetFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories);
            var releaseFiles = new ConcurrentDictionary<string, string>();
            await Parallel.ForEachAsync(files, cancellationToken, async (file, token) =>
            {
                try
                {
                    using var fs = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var sha256 = await SHA256.HashDataAsync(fs, cancellationToken);
                    lock (releaseFiles)
                    {
                        releaseFiles[Convert.ToHexStringLower(sha256)] = file;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Calculate file hash failed: {file}", file);
                }
            });
            _currentVersionFilesHash = releaseFiles;
        }
    }



    #endregion



    #region Update



    private async Task UpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            BackupVersionIni();
            State = UpdateState.Downloading;
            await DownloadFilesAsync(cancellationToken);
            State = UpdateState.Pending;
            await PatchAndVerifyAsync(cancellationToken);
            DeleteOldFiles();
            DeleteUpdateCacheFolder();
            await Task.Delay(1000, cancellationToken);
            State = UpdateState.Finish;
            _logger.LogInformation("Update Starward finished");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Update Starward canceled, restore version.ini");
            State = UpdateState.Stop;
            RestoreVersionIni();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update Starward failed, restore version.ini");
            State = UpdateState.Error;
            ErrorMessage = ex.Message;
            RestoreVersionIni();
        }
    }



    private void BackupVersionIni()
    {
        var ini = Path.Combine(_targetPath, "version.ini");
        var backup = Path.Combine(_updateCacheFolder, "version.ini");
        if (File.Exists(backup))
        {
            File.Delete(backup);
        }
        if (File.Exists(ini))
        {
            File.Copy(ini, Path.Combine(_updateCacheFolder, "version.ini"), true);
        }
    }



    private void RestoreVersionIni()
    {
        var ini = Path.Combine(_updateCacheFolder, "version.ini");
        if (File.Exists(ini))
        {
            File.Copy(ini, Path.Combine(_targetPath, "version.ini"), true);
        }
    }



    private async Task DownloadFilesAsync(CancellationToken cancellationToken = default)
    {
        _progress_DownloadBytes = 0;
        Progress_TotalBytes = 0;
        if (_releaseManifest.DiffVersion is null)
        {
            foreach (var item in _releaseManifest.Files)
            {
                if (!(_currentVersionFilesHash?.TryGetValue(item.Hash, out var value) ?? false))
                {
                    Progress_TotalBytes += item.CompressedSize;
                }
            }
        }
        else
        {
            Progress_TotalBytes = _releaseManifest.DiffSize;
        }

        List<(string Id, long Size, string Hash, string? Suffix)> list = new();
        foreach (var item in _releaseManifest.Files)
        {
            if (item.Patch is null && !_currentVersionFilesHash!.ContainsKey(item.Hash))
            {
                list.Add((item.Id, item.CompressedSize, item.CompressedHash, item.UrlSuffix));
            }
            else if (item.Patch?.Id is not null && item.Patch?.PatchHash is not null)
            {
                list.Add((item.Patch.Id, item.Patch.PatchSize, item.Patch.PatchHash, item.Patch.UrlSuffix));
            }
        }
        list = list.DistinctBy(x => x.Id).ToList();

        _logger.LogInformation("Update Starward, downloading {count} files, file size: {size}", _releaseManifest.DiffFileCount, Progress_TotalBytes);
        await Parallel.ForEachAsync(list, cancellationToken, async (item, token) =>
        {
            await _polly.ExecuteAsync(async (pollyToken) => await DownloadReleaseFileAsync(item, pollyToken), token);
        });
        _logger.LogInformation("Update Starward, downloading files finished");
    }



    private async Task PatchAndVerifyAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Update Starward, patching and verifying files");
        await Parallel.ForEachAsync(_releaseManifest.Files, cancellationToken, async (releaseFile, token) =>
        {
            try
            {
                await DecompressOrPatchFileAsync(releaseFile, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Decompress or patch file failed: {file}", releaseFile.Path);
            }
            await _polly.ExecuteAsync(async (pollyToken) => await VerifyAndDownloadFileAsync(releaseFile, pollyToken), token);
        });
    }



    private async Task DownloadReleaseFileAsync((string Id, long Size, string Hash, string? Suffix) item, CancellationToken cancellationToken = default)
    {
        string url = _releaseManifest.UrlPrefix + item.Id + (item.Suffix ?? _releaseManifest.UrlSuffix);
        string path = Path.Combine(_updateCacheFolder, item.Id);

        using var fs = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        // 這一輪加進進度的位元組，失敗時扣回去，Polly 重試時才不會把已下載的部分再算一次
        long progress = fs.Length;
        Interlocked.Add(ref _progress_DownloadBytes, progress);
        if (fs.Length < item.Size)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url) { VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
                request.Headers.Range = new RangeHeaderValue(fs.Length, null);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentRange?.From is not null)
                {
                    fs.Position = response.Content.Headers.ContentRange.From.Value;
                }
                // 連線無聲斷掉時讀取會一直卡住，逾時後交給 Polly 重試，從已寫入的長度續傳
                using var hs = new IdleTimeoutStream(await response.Content.ReadAsStreamAsync(cancellationToken));
                var buffer = new byte[1 << 16];
                int length;
                while ((length = await hs.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, length), cancellationToken);
                    Interlocked.Add(ref _progress_DownloadBytes, length);
                    progress += length;
                }
            }
            catch (Exception ex)
            {
                Interlocked.Add(ref _progress_DownloadBytes, -progress);
                if (ex is TaskCanceledException { InnerException: TimeoutException })
                {
                    // HttpClient.Timeout 到期丟的是 TaskCanceledException，Polly 預設不重試取消，外層也會當成使用者取消
                    throw new TimeoutException(ex.Message, ex);
                }
                if (ex is TimeoutException)
                {
                    _logger.LogWarning("Download stalled at {position} bytes: {url} ({message})", fs.Length, url, ex.Message);
                }
                throw;
            }
        }
        await fs.FlushAsync(cancellationToken);

        fs.Position = 0;
        var sha256 = await SHA256.HashDataAsync(fs, cancellationToken);
        if (string.Equals(Convert.ToHexString(sha256), item.Hash, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _logger.LogWarning("Checksum failed: {path}", path);
        fs.Dispose();
        File.Delete(path);
        Interlocked.Add(ref _progress_DownloadBytes, -progress);
        throw new Exception($"Checksum failed: {path}");
    }



    private async Task DecompressOrPatchFileAsync(ReleaseFile releaseFile, CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(_targetPath, releaseFile.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (releaseFile.Patch is null && _currentVersionFilesHash.TryGetValue(releaseFile.Hash, out string? oldPath))
        {
            File.Copy(oldPath, path, true);
        }
        else if (releaseFile.Patch is null)
        {
            string downloadPath = Path.Combine(_updateCacheFolder, releaseFile.Id);
            if (File.Exists(downloadPath))
            {
                using var zstdFs = File.OpenRead(downloadPath);
                using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var decompressionStream = new ZstdSharp.DecompressionStream(zstdFs);
                await decompressionStream.CopyToAsync(fs, cancellationToken);
            }
        }
        else if (_currentVersionFilesHash.TryGetValue(releaseFile.Patch.OldFileHash, out string? oldPath2) && new FileInfo(oldPath2).Length == releaseFile.Patch.OldFileSize)
        {
            if (releaseFile.Patch.Id is null)
            {
                File.Copy(oldPath2, path, true);
            }
            else
            {
                string patchPath = Path.Combine(_updateCacheFolder, releaseFile.Patch.Id);
                if (File.Exists(patchPath))
                {
                    using var oldFs = File.Open(oldPath2, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var newFs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    using var patchFs = new FileSliceStream(patchPath, releaseFile.Patch.Offset, releaseFile.Patch.Length == 0 ? releaseFile.Patch.PatchSize : releaseFile.Patch.Length);
                    HPatch.PatchZstandard(oldFs, patchFs, newFs);
                }
            }
        }
    }



    private async Task VerifyAndDownloadFileAsync(ReleaseFile releaseFile, CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(_targetPath, releaseFile.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length != releaseFile.Size)
        {
            string url = _releaseManifest.UrlPrefix + releaseFile.Id + _releaseManifest.UrlSuffix;
            try
            {
                // 連線無聲斷掉時讀取會一直卡住，逾時後交給 Polly 重試，重新下載這個檔案
                using var hs = new IdleTimeoutStream(await _httpClient.GetStreamAsync(url, cancellationToken));
                fs.SetLength(0);
                using var zstdStream = new ZstdSharp.DecompressionStream(hs);
                await zstdStream.CopyToAsync(fs, cancellationToken);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                // HttpClient.Timeout 到期丟的是 TaskCanceledException，Polly 預設不重試取消，外層也會當成使用者取消
                throw new TimeoutException(ex.Message, ex);
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning("Download stalled after {position} bytes: {url} ({message})", fs.Length, url, ex.Message);
                throw;
            }
            fs.Position = 0;
        }
        var sha256 = await SHA256.HashDataAsync(fs, cancellationToken);
        if (!string.Equals(Convert.ToHexStringLower(sha256), releaseFile.Hash, StringComparison.OrdinalIgnoreCase))
        {
            fs.Dispose();
            File.Delete(path);
            throw new Exception($"Checksum failed: {path}");
        }
    }



    private void DeleteOldFiles()
    {
        try
        {
            if (_releaseManifest.DeleteFiles is null)
            {
                return;
            }
            foreach (var item in _releaseManifest.DeleteFiles)
            {
                string path = Path.Combine(_targetPath, item);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete old files");
        }
    }



    private void DeleteUpdateCacheFolder()
    {
        try
        {
            Directory.Delete(_updateCacheFolder, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete update cache folder");
        }
    }



    #endregion



}
