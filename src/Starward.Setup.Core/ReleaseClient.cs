using Starward.Setup.Core.Github;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Setup.Core;

public class ReleaseClient
{
    public static Uri DefaultBaseAddress { get; set; } = new("https://starward-release.scighost.com");

    /// <summary>
    /// 發佈版本的 GitHub 倉庫，本分支的更新與更新說明都從這裡取
    /// </summary>
    public const string GithubRepository = "funyhao0930/Starward";


    private readonly HttpClient _httpClient;

    public ReleaseClient(HttpClient httpClient)
    {
        if (httpClient is null)
        {
            _httpClient = new HttpClient(new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                EnableMultipleHttp2Connections = true,
                EnableMultipleHttp3Connections = true,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });
            _httpClient.DefaultRequestHeaders.Add("User-Agent", $"{Path.GetFileNameWithoutExtension(Environment.ProcessPath)}/*");
        }
        else
        {
            _httpClient = httpClient;
        }
        _httpClient.BaseAddress = DefaultBaseAddress;
    }


    public async Task<ReleaseInfo> GetLatestReleaseInfoAsync(bool isPrerelease, string currentVersion, CancellationToken cancellationToken = default)
    {
        var url = isPrerelease switch
        {
            false => $"/release/latest?version={currentVersion}",
            true => $"/release/latest-preview?version={currentVersion}",
        };
        var info = await _httpClient.GetFromJsonAsync(url, ReleaseJsonContext.Default.ReleaseInfo, cancellationToken);
        return info ?? throw new NullReferenceException($"Cannot get json content from '{url}'.");
    }


    public async Task<ReleaseInfoDetail> GetLatestReleaseInfoDetailAsync(bool isPrerelease, string currentVersion, Architecture arch, InstallType type, CancellationToken cancellationToken = default)
    {
        var url = isPrerelease switch
        {
            false => $"/release/latest?version={currentVersion}",
            true => $"/release/latest-preview?version={currentVersion}",
        };
        var info = await _httpClient.GetFromJsonAsync(url, ReleaseJsonContext.Default.ReleaseInfo, cancellationToken);
        if (info is null)
        {
            throw new NullReferenceException($"Cannot get json content from '{url}'.");
        }
        string key = $"{arch}-{type}".ToLower();
        if (info.Releases?.TryGetValue(key, out var value) ?? false)
        {
            return value;
        }
        else
        {
            throw new PlatformNotSupportedException($"Platform ({arch}, {type}) is not supported.");
        }
    }

    public async Task<ReleaseInfo> GetReleaseInfoAsync(string version, CancellationToken cancellationToken = default)
    {
        var url = $"/release/version/{version}";
        var info = await _httpClient.GetFromJsonAsync(url, ReleaseJsonContext.Default.ReleaseInfo, cancellationToken);
        return info ?? throw new NullReferenceException($"Cannot get json content from '{url}'.");
    }


    public async Task<ReleaseManifest> GetReleaseManifestAsync(string url, CancellationToken cancellationToken = default)
    {
        var manifest = await _httpClient.GetFromJsonAsync(url, ReleaseJsonContext.Default.ReleaseManifest, cancellationToken);
        return manifest ?? throw new NullReferenceException($"Cannot get json content from '{url}'.");
    }



    #region Github



    public async Task<GithubRelease?> GetGithubLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        const string url = $"https://api.github.com/repos/{GithubRepository}/releases?page=1&per_page=1";
        var list = await _httpClient.GetFromJsonAsync(url, ReleaseJsonContext.Default.ListGithubRelease, cancellationToken);
        return list?.FirstOrDefault();
    }



    public async Task<List<GithubRelease>> GetGithubReleaseAsync(int page, int perPage, CancellationToken cancellationToken = default)
    {
        string url = $"https://api.github.com/repos/{GithubRepository}/releases?page={page}&per_page={perPage}";
        var list = await _httpClient.GetFromJsonAsync(url, ReleaseJsonContext.Default.ListGithubRelease, cancellationToken);
        return list ?? new List<GithubRelease>();
    }



    public async Task<GithubRelease?> GetGithubReleaseAsync(string tag, CancellationToken cancellationToken = default)
    {
        string url = $"https://api.github.com/repos/{GithubRepository}/releases/tags/{tag}";
        return await _httpClient.GetFromJsonAsync(url, ReleaseJsonContext.Default.GithubRelease, cancellationToken);
    }


    public async Task<string> RenderGithubMarkdownAsync(string markdown, CancellationToken cancellationToken = default)
    {
        const string url = "https://api.github.com/markdown";
        var request = new GithubMarkdownRequest
        {
            Text = markdown,
            Mode = "gfm",
            Context = GithubRepository,
        };
        var content = new StringContent(JsonSerializer.Serialize(request, ReleaseJsonContext.Default.GithubMarkdownRequest), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }



    /// <summary>
    /// 把 GitHub Release 轉成更新資訊：可攜版取 7z、安裝版取安裝程式，雜湊從同一版的 SHA256SUMS.txt 讀。
    /// 沒有逐檔 manifest，<see cref="ReleaseInfoDetail.ManifestUrl"/> 為 null，更新時整包下載。
    /// </summary>
    public async Task<ReleaseInfoDetail> GetGithubReleaseInfoDetailAsync(GithubRelease release, Architecture arch, InstallType type, CancellationToken cancellationToken = default)
    {
        string version = release.TagName;
        string packageName = type is InstallType.Setup
            ? $"Starward_Setup_{version}_{arch.ToString().ToLower()}.exe"
            : $"Starward_Portable_{version}_{arch.ToString().ToLower()}.7z";
        GithubAsset package = release.Assets?.FirstOrDefault(x => string.Equals(x.Name, packageName, StringComparison.OrdinalIgnoreCase))
            ?? throw new PlatformNotSupportedException($"Release {version} has no package '{packageName}'.");
        GithubAsset sums = release.Assets?.FirstOrDefault(x => string.Equals(x.Name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Release {version} has no SHA256SUMS.txt.");

        string sumsText = await _httpClient.GetStringAsync(sums.BrowserDownloadUrl, cancellationToken);
        string? hash = null;
        foreach (string line in sumsText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // sha256sum 的格式：<hash>  <檔名>，二進位模式檔名前會多一個 *
            string[] parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && string.Equals(parts[1].TrimStart('*'), packageName, StringComparison.OrdinalIgnoreCase))
            {
                hash = parts[0].ToLowerInvariant();
                break;
            }
        }
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new InvalidDataException($"SHA256SUMS.txt of release {version} has no entry for '{packageName}'.");
        }

        return new ReleaseInfoDetail
        {
            Version = version,
            Architecture = arch,
            InstallType = type,
            BuildTime = release.PublishedAt,
            PackageUrl = package.BrowserDownloadUrl,
            PackageSize = package.Size,
            PackageHash = hash,
            ManifestUrl = null!,
            Setup = type is InstallType.Setup ? new ReleaseSetup
            {
                Url = package.BrowserDownloadUrl,
                FileName = package.Name,
                Size = package.Size,
                Hash = hash,
            } : null,
            Diffs = new(),
        };
    }



    #endregion


}
