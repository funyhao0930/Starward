using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Features.RPC;
using Starward.Helpers;
using Starward.Setup.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Update;

internal class SetupService
{

    private readonly ILogger<SetupService> _logger;

    private readonly HttpClient _httpClient;

    private readonly ReleaseClient _releaseClient;


    public SetupService(ILogger<SetupService> logger, HttpClient httpClient, ReleaseClient releaseClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _releaseClient = releaseClient;
    }


    public long SetupTotalBytes { get; private set; }

    public long SetupDownloadBytes { get; private set; }



    private async Task<ReleaseInfoDetail> GetReleaseInfoDetailAsync(CancellationToken cancellationToken = default)
    {
        return await _releaseClient.GetLatestGithubReleaseInfoDetailAsync(AppConfig.EnablePreviewRelease, RuntimeInformation.ProcessArchitecture, AppConfig.InstallType, cancellationToken);
    }



    public async Task<string?> DownloadSetupAsync(ReleaseInfoDetail? detail, CancellationToken cancellationToken = default)
    {
        detail ??= await GetReleaseInfoDetailAsync(cancellationToken);

        if (detail?.Setup is null)
        {
            return null;
        }

        string setupPath = Path.Combine(AppConfig.CacheFolder, detail.Setup.FileName);
        string url = detail.Setup.Url;
        long size = detail.Setup.Size;
        string hash = detail.Setup.Hash;

        if (File.Exists(setupPath))
        {
            if (await FileHashHelper.CheckSHA256Async(setupPath, size, hash, cancellationToken))
            {
                return setupPath;
            }
            File.Delete(setupPath);
        }

        SetupTotalBytes = detail.Setup.Size;
        await DownloadFileAsync(setupPath, url, size, hash, cancellationToken);
        return setupPath;
    }


    private async Task DownloadFileAsync(string path, string url, long size, string hash, CancellationToken cancellationToken = default)
    {
        using var fs = File.Open(path, FileMode.OpenOrCreate);
        await DownloadFileAsync(fs, url, size, hash, cancellationToken);
    }


    private async Task DownloadFileAsync(Stream stream, string url, long size, string hash, CancellationToken cancellationToken = default)
    {
        bool success = false;
        for (int i = 0; i < 3; i++)
        {
            SetupDownloadBytes = stream.Length;
            if (stream.Length < SetupTotalBytes)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(stream.Length, null);
                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    // 伺服器不支援續傳時會回整個檔案
                    stream.Position = response.Content.Headers.ContentRange?.From ?? 0;
                    stream.SetLength(stream.Position);
                    SetupDownloadBytes = stream.Position;
                    // 連線無聲斷掉時讀取會一直卡住，逾時後重試，從已寫入的長度續傳
                    using var hs = new IdleTimeoutStream(await response.Content.ReadAsStreamAsync(cancellationToken));
                    int read = 0;
                    Memory<byte> buffer = new byte[8192];
                    while ((read = await hs.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await stream.WriteAsync(buffer[..read], cancellationToken);
                        SetupDownloadBytes += read;
                    }
                    SetupDownloadBytes = stream.Length;
                }
                catch (Exception ex) when (i < 2 && !cancellationToken.IsCancellationRequested)
                {
                    // 讀取逾時、連線中斷、HttpClient.Timeout 到期都在這裡，已下載的部分留著，下一輪續傳
                    _logger.LogWarning(ex, "Download setup file failed at {position} bytes, retrying.", stream.Length);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    // 最後一次也失敗了。HttpClient.Timeout 到期丟的是 TaskCanceledException，呼叫端會當成使用者取消
                    throw new TimeoutException(ex.Message, ex);
                }
            }
            stream.Position = 0;
            if (await FileHashHelper.CheckSHA256Async(stream, hash, cancellationToken))
            {
                success = true;
                break;
            }
            stream.SetLength(0);
        }
        if (!success)
        {
            throw new Exception("Setup file checksum mismatched.");
        }
    }



    public async Task UpdateAsync(ReleaseInfoDetail detail, CancellationToken cancellationToken = default)
    {
        string? setupPath = await DownloadSetupAsync(detail, cancellationToken);
        if (!File.Exists(setupPath))
        {
            throw new NotSupportedException("Update is not supported.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        // 本分支的安裝程式內嵌完整封包，直接跑一般安裝流程覆蓋；它的 update 模式會去上游伺服器取版本
        Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            UseShellExecute = true,
            Verb = "runas",
        });
        // 安裝程式要覆蓋目前的檔案，交給它之後就結束，RPC 也一起關掉
        AppConfig.GetService<RpcService>().KeepRunningOnExited(false, noLongerChange: true);
        Environment.Exit(0);
    }




}
