using Starward.Core;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Hashing;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ZiggyCreatures.Caching.Fusion;

namespace Starward.Helpers;


internal static class FileCache
{

    private const int RETRY_COUNT = 2;

    private static readonly FusionCache _fusionCache;

    private static readonly HttpClient _httpClient;


    static FileCache()
    {
        _fusionCache = new FusionCache(new FusionCacheOptions
        {
            DefaultEntryOptions = new FusionCacheEntryOptions
            {
                Duration = TimeSpan.MaxValue,
                FactoryHardTimeout = TimeSpan.FromMinutes(1),
                AllowTimedOutFactoryBackgroundCompletion = false,
            },
        });
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            EnableMultipleHttp3Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }



    public static string? CacheFolder { get; private set; }



    public static bool Initialize(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            CacheFolder = folder;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error initializing FileCache2: {ex.Message}");
        }
        return false;
    }



    public static async Task<string?> GetFromCacheAsync(string url, bool throwOnError = false, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _fusionCache.GetOrSetAsync(url, token => GetFromCacheOrDownloadAsync(url, token), token: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error retrieving file from cache: {ex.Message}");
            if (throwOnError)
            {
                throw;
            }
        }
        return null;
    }



    private static async Task<string> GetFromCacheOrDownloadAsync(string url, CancellationToken cancellationToken)
    {
        if (CacheFolder is null)
        {
            throw new DirectoryNotFoundException("Cache folder not initialized.");
        }
        string filePath = Path.Combine(CacheFolder, GetCacheFileName(url));
        if (new FileInfo(filePath) is { Exists: true, Length: > 0 })
        {
            return filePath;
        }
        // 下载成功就返回，只有失败才重试。读取超时、连接中断都会留下 .tmp，下一次用 Range 续传
        // 调用方取消不重试；没取消的 OperationCanceledException 也算网络失败，重试
        // 最后一次的异常一定要抛出，返回 null 会被 FusionCache 永久缓存，之后这个网址再也不会重新下载
        for (int i = 0; ; i++)
        {
            try
            {
                await DownloadFileAsync(url, filePath, cancellationToken).ConfigureAwait(false);
                return filePath;
            }
            catch (Exception ex) when (i < RETRY_COUNT && IsRetryable(ex, cancellationToken))
            {
                Debug.WriteLine($"Download failed (attempt {i + 1}/{RETRY_COUNT + 1}), retrying: {ex.Message}");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200 * (i + 1)), cancellationToken).ConfigureAwait(false);
        }
    }


    private static bool IsRetryable(Exception ex, CancellationToken cancellationToken)
    {
        return ex switch
        {
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            HttpRequestException or TimeoutException or IOException => true,
            _ => false,
        };
    }



    private static async Task DownloadFileAsync(string url, string path, CancellationToken cancellationToken)
    {
        string tmpPath = path + ".tmp";

        using (FileStream fs = File.Open(tmpPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(fs.Length, null);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentRange is { From: > 0 } range)
            {
                fs.Position = range.From.Value;
            }
            else
            {
                fs.Position = 0;
                fs.SetLength(0);
            }
            // 连接断了时读取会一直挂着，之后同一个网址的请求也都在等这个任务。超时后这次失败，.tmp 留着下次续传
            using var hs = new IdleTimeoutStream(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
            await hs.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
            await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(tmpPath, path, true);
    }


    private static string GetCacheFileName(string url)
    {
        byte[] hashBytes = ArrayPool<byte>.Shared.Rent(24);
        try
        {
            ReadOnlySpan<byte> pathSpan = MemoryMarshal.AsBytes(url.AsSpan());
            XxHash64.Hash(pathSpan, hashBytes.AsSpan(0, 8));
            MD5.HashData(pathSpan, hashBytes.AsSpan(8, 16));
            return Convert.ToHexString(hashBytes.AsSpan(0, 24));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(hashBytes);
        }
    }


    public static async void DeleteCacheFile(string url)
    {
        try
        {
            await _fusionCache.RemoveAsync(url).ConfigureAwait(false);
            await Task.Run(() =>
            {
                if (CacheFolder is null)
                {
                    return;
                }
                string filePath = Path.Combine(CacheFolder, GetCacheFileName(url));
                DeleteFileIfExists(filePath);
                DeleteFileIfExists(filePath + ".tmp");
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error deleting cache file: {ex.Message}");
        }
    }



    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error deleting cache file: {ex.Message}");
        }
    }



}
