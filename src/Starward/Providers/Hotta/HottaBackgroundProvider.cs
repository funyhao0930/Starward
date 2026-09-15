using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using Starward.Core.Games.Hotta;
using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Hotta;
using Starward.Features.Background;
using Starward.Features.GameLauncher;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Hotta;

/// <summary>
/// 异环的在线背景图，来自官方更新程序的文件清单。
/// <para/>
/// 在这之前异环走的是纯本机路线：把安装目录下的 <c>bgimgs</c> 当作背景美术目录，
/// 由 <see cref="Core.Games.LocalGameArtwork"/> 挑出最新的一个文件。那条路有三个问题：
/// <list type="bullet">
/// <item>目录里作废的旧文件（配置里的 <c>discards</c>）也在，按时间挑会挑到它们；</item>
/// <item>目录路径写死了 GameID <c>2000013</c>，换版本或代理商就指不到；</item>
/// <item>挑出来的是单个文件，走的是「自定义背景」那条分支，因此只有视频没有首帧图，
/// 主题色也就无从算起。</item>
/// </list>
/// 清单这条路把这三点一并解决：配置文件指名当期该用哪张图、哪段视频，
/// 路径由清单给出，静态图与视频成对返回，显示层就能按动态背景正常处理。
/// <para/>
/// 已经装了游戏的玩家不必重新下载：本机文件的校验值与清单一致时直接复制过去。
/// </summary>
internal class HottaBackgroundProvider : IGameBackgroundProvider
{

    private readonly HottaLauncherClient _client;

    private readonly IMemoryCache _memoryCache;

    private readonly ILogger<HottaBackgroundProvider> _logger;


    public HottaBackgroundProvider(HottaLauncherClient client, IMemoryCache memoryCache, ILogger<HottaBackgroundProvider> logger)
    {
        _client = client;
        _memoryCache = memoryCache;
        _logger = logger;
    }


    public string ProviderId => HottaGameMapping.ProviderId;


    public bool Supports(GameKey key) => key == HottaGameMapping.NevernessToEvernessTaiwan;


    public async Task<List<GameBackground>> GetBackgroundsAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (!Supports(key))
        {
            return [];
        }
        // 与其他供应商一样留一分钟的短缓存：切换游戏会反复问同一份清单
        string cacheKey = $"{nameof(HottaBackgroundProvider)}_{key}";
        if (_memoryCache.TryGetValue(cacheKey, out GameBackground? cached) && cached is not null)
        {
            return [cached];
        }

        string? installPath = GameLauncherService.GetGameInstallPath(key);
        // 起点是游戏自己的 Config.ini：地址由它给出，没装游戏就没有起点。
        // 不在这里写死地址，理由见 HottaGameMapping.ParseVersionInfoUrls。
        IReadOnlyList<string> versionInfoUrls = HottaGameMapping.ParseVersionInfoUrls(await ReadConfigFileAsync(installPath, cancellationToken));
        if (versionInfoUrls.Count is 0)
        {
            return [];
        }

        string? fileListUrl = await _client.GetFileListUrlAsync(versionInfoUrls, cancellationToken);
        if (string.IsNullOrWhiteSpace(fileListUrl))
        {
            return [];
        }
        // 整份清单有上千个文件，只留背景目录里的那几个
        HottaFileManifest? manifest = await _client.GetFileManifestAsync(fileListUrl, HottaBackgroundMapper.IsBackgroundFile, cancellationToken);
        if (manifest is null)
        {
            return [];
        }
        if (HottaBackgroundMapper.FindConfigFile(manifest) is not HottaManifestFile configFile)
        {
            return [];
        }
        HottaBackgroundConfig? config = await _client.GetBackgroundConfigAsync(manifest, configFile, cancellationToken);
        (HottaManifestFile? poster, HottaManifestFile? video) = HottaBackgroundMapper.SelectFiles(config, manifest);
        if (poster is null)
        {
            return [];
        }

        string? posterName = await EnsureCachedAsync(manifest, poster, installPath, cancellationToken);
        // 视频拿不到不致命，静态背景仍然可用
        string? videoName = video is null ? null : await EnsureCachedAsync(manifest, video, installPath, cancellationToken);
        GameBackground? background = HottaBackgroundMapper.ToGameBackground(poster, video, posterName, videoName);
        if (background is null)
        {
            return [];
        }
        _memoryCache.Set(cacheKey, background, TimeSpan.FromMinutes(1));
        return [background];
    }


    /// <summary>
    /// 确保这个文件已经在背景图缓存目录里，返回缓存中的文件名；失败返回 null。
    /// <para/>
    /// 返回的是文件名而不是链接：显示层统一用
    /// <see cref="BackgroundService.GetBackgroundFileAsync"/> 取文件，
    /// 而那个方法按文件名在缓存目录里找，找到就不会再去下载。
    /// 异环的文件在服务器上是压缩包，走不了普通的下载流程，因此在这里先放好。
    /// </summary>
    private async Task<string?> EnsureCachedAsync(HottaFileManifest manifest, HottaManifestFile file,
                                                  string? installPath, CancellationToken cancellationToken)
    {
        try
        {
            string name = HottaBackgroundMapper.GetCacheFileName(file);
            string path = BackgroundService.GetBgFilePath(name);
            if (File.Exists(path) && new FileInfo(path).Length == file.Size)
            {
                return name;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // 已经装了游戏的玩家本机就有这个文件，校验值一致时直接复制，不必再下一遍
            if (TryGetLocalFile(installPath, file) is string local)
            {
                File.Copy(local, path, true);
                return name;
            }
            byte[] bytes = await _client.GetFileAsync(manifest, file, cancellationToken);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return name;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache Neverness to Everness background file ({path})", file.Path);
            return null;
        }
    }


    /// <summary>
    /// 本机安装目录里内容一致的同一个文件，没有或对不上返回 null
    /// </summary>
    private string? TryGetLocalFile(string? installPath, HottaManifestFile file)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }
        try
        {
            // 清单里的路径相对于游戏本体所在的那一层，也就是安装目录下的 NTETW
            string path = Path.Join(installPath, HottaGameMapping.GameFolderName, file.Path.TrimStart('/'));
            if (!File.Exists(path))
            {
                return null;
            }
            var info = new FileInfo(path);
            // 先比字节数，对不上就不必再算校验值
            if (info.Length != file.Size)
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(file.Checksum))
            {
                return null;
            }
            using FileStream fs = File.OpenRead(path);
            string md5 = Convert.ToHexString(MD5.HashData(fs)).ToLowerInvariant();
            return md5.Equals(file.Checksum, StringComparison.OrdinalIgnoreCase) ? path : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Check local Neverness to Everness background file ({path})", file.Path);
            return null;
        }
    }


    private static async Task<string?> ReadConfigFileAsync(string? installPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }
        string path = Path.Combine(installPath, HottaGameMapping.ConfigFileRelativePath);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }

}
