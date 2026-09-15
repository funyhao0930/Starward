using System.IO.Compression;
using System.Net;
using System.Text.Json;

namespace Starward.Core.Launcher.Hotta;

/// <summary>
/// 异环官方更新程序的在线客户端。
/// <para/>
/// 与米哈游、库洛、鹰角都不同：这家没有 JSON 接口，只有老式的版本文件与文件清单，
/// 而且每个文件都以 <c>.zip</c> 的形式发布——直接请求原始路径服务器返回 403。
/// 因此这里比其他几家多一层解压。
/// <para/>
/// 所有地址都不写死：起点是游戏自己 Config.ini 里给出的 Version.ini 地址，
/// 之后每一步的地址都由上一步的文件给出。换了代理商或域名不必改这里。
/// </summary>
public class HottaLauncherClient
{

    private readonly HttpClient _httpClient;


    public HottaLauncherClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
    }


    /// <summary>
    /// 文件清单的地址，取自 Version.ini。主站不通就换备援，都不通返回 null。
    /// </summary>
    /// <param name="versionInfoUrls">Version.ini 的地址，主站在前，见 <c>HottaGameMapping.ParseVersionInfoUrls</c></param>
    public async Task<string?> GetFileListUrlAsync(IReadOnlyList<string> versionInfoUrls, CancellationToken cancellationToken = default)
    {
        foreach (string url in versionInfoUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string text = await _httpClient.GetStringAsync(url, cancellationToken);
                if (HottaLauncherManifest.ParseFileListUrl(text) is string fileListUrl)
                {
                    return fileListUrl;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                // 主站不通就换备援
            }
            catch (OperationCanceledException)
            {
                // HttpClient 自己超时抛的也是这个类型，同样换备援
            }
        }
        return null;
    }


    /// <summary>
    /// 文件清单，取不到返回 null。
    /// <para/>
    /// 整份清单有上千个文件、三万多字节，因此在解析时就按 <paramref name="pathFilter"/> 筛。
    /// </summary>
    public async Task<HottaFileManifest?> GetFileManifestAsync(string fileListUrl, Func<string, bool>? pathFilter = null,
                                                               CancellationToken cancellationToken = default)
    {
        string xml = await _httpClient.GetStringAsync(fileListUrl, cancellationToken);
        return HottaLauncherManifest.ParseFileManifest(xml, pathFilter);
    }


    /// <summary>
    /// 下载清单里的一个文件并解压，返回解压后的内容。
    /// <para/>
    /// 压缩包里只有一个文件，就是这个文件本身。
    /// </summary>
    public async Task<byte[]> GetFileAsync(HottaFileManifest manifest, HottaManifestFile file, CancellationToken cancellationToken = default)
    {
        byte[] zipped = await _httpClient.GetByteArrayAsync(manifest.GetDownloadUrl(file), cancellationToken);
        using var stream = new MemoryStream(zipped);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        ZipArchiveEntry entry = archive.Entries.FirstOrDefault(x => x.Name.Equals(file.FileName, StringComparison.OrdinalIgnoreCase))
                             ?? archive.Entries.FirstOrDefault()
                             ?? throw new InvalidDataException($"The archive of '{file.Path}' is empty.");
        using var entryStream = entry.Open();
        using var buffer = new MemoryStream();
        await entryStream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }


    /// <summary>
    /// 下载并解析背景配置，取不到返回 null
    /// </summary>
    public async Task<HottaBackgroundConfig?> GetBackgroundConfigAsync(HottaFileManifest manifest, HottaManifestFile configFile,
                                                                       CancellationToken cancellationToken = default)
    {
        byte[] bytes = await GetFileAsync(manifest, configFile, cancellationToken);
        using var stream = new MemoryStream(bytes);
        return await JsonSerializer.DeserializeAsync(stream, typeof(HottaBackgroundConfig), HottaLauncherJsonContext.Default, cancellationToken) as HottaBackgroundConfig;
    }

}
