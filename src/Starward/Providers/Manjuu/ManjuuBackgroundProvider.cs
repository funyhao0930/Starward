using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using Starward.Core.Games.Manjuu;
using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Manjuu;
using Starward.Features.Background;
using Starward.Features.GameLauncher;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Starward.Providers.Manjuu;

/// <summary>
/// 蓝色星原的首页背景，来自官方启动器缓存的 <c>AppData\data\cache.json</c>。
/// <para/>
/// 官方启动器的首页接口要签名，这份缓存记着当期背景图与视频的 CDN 地址和本机文件名，
/// 是不必签名的来源。不直接把媒体缓存目录当成背景美术目录，因为：
/// <list type="bullet">
/// <item>目录里还放着 Logo，按「最新的图片」挑会挑到它；</item>
/// <item>视频的扩展名是 .dat，显示层不认得；</item>
/// <item>背景图照启动器窗口形状挖了一条透明的标题栏缺口和圆角，直接铺满会露出一条空白。</item>
/// </list>
/// 因此这里把两者复制进 Starward 的背景缓存：视频改回 .mp4，背景图裁掉透明的边。
/// 本机文件不在（例如启动器还没下载过）时才从 CDN 下载。
/// </summary>
internal class ManjuuBackgroundProvider : IGameBackgroundProvider
{

    private readonly HttpClient _httpClient;

    private readonly IMemoryCache _memoryCache;

    private readonly ILogger<ManjuuBackgroundProvider> _logger;


    public ManjuuBackgroundProvider(HttpClient httpClient, IMemoryCache memoryCache, ILogger<ManjuuBackgroundProvider> logger)
    {
        _httpClient = httpClient;
        _memoryCache = memoryCache;
        _logger = logger;
    }


    public string ProviderId => ManjuuGameMapping.ProviderId;


    public bool Supports(GameKey key) => key == ManjuuGameMapping.AzurPromiliaTC;


    public async Task<List<GameBackground>> GetBackgroundsAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (!Supports(key))
        {
            return [];
        }
        // 与其他供应商一样留一分钟的短缓存：切换游戏会反复来问
        string cacheKey = $"{nameof(ManjuuBackgroundProvider)}_{key}";
        if (_memoryCache.TryGetValue(cacheKey, out GameBackground? cached) && cached is not null)
        {
            return [cached];
        }

        string? installPath = GameLauncherService.GetGameInstallPath(key);
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return [];
        }
        string dataFolder = Path.Join(installPath, ManjuuGameMapping.LauncherDataFolderName);
        string cacheFile = Path.Join(dataFolder, ManjuuLauncherCache.RelativePath);
        if (!File.Exists(cacheFile))
        {
            return [];
        }
        ManjuuLauncherCache? cache = ManjuuLauncherCache.Parse(await File.ReadAllTextAsync(cacheFile, cancellationToken));
        string mediaFolder = Path.Join(dataFolder, ManjuuLauncherCache.MediaCacheFolderName);

        string? posterName = await EnsurePosterAsync(cache, mediaFolder, cancellationToken);
        // 视频拿不到不致命，静态背景仍然可用
        string? videoName = await EnsureVideoAsync(cache, mediaFolder, cancellationToken);
        GameBackground? background = ManjuuBackgroundMapper.ToGameBackground(cache, posterName, videoName);
        if (background is null)
        {
            return [];
        }
        _memoryCache.Set(cacheKey, background, TimeSpan.FromMinutes(1));
        return [background];
    }


    /// <summary>
    /// 把静态背景裁好放进缓存目录，返回缓存中的文件名；失败返回 null
    /// </summary>
    private async Task<string?> EnsurePosterAsync(ManjuuLauncherCache? cache, string mediaFolder, CancellationToken cancellationToken)
    {
        if (ManjuuBackgroundMapper.GetPosterCacheName(cache) is not string name)
        {
            return null;
        }
        try
        {
            string path = BackgroundService.GetBgFilePath(name);
            if (File.Exists(path))
            {
                return name;
            }
            byte[]? bytes = await ReadLocalOrDownloadAsync(mediaFolder, cache!.BackgroundName, cache.BackgroundUrl, cancellationToken);
            if (bytes is null)
            {
                return null;
            }
            byte[] png = await CropTransparentEdgesAsync(bytes);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, png, cancellationToken);
            return name;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache Azur Promilia background image");
            return null;
        }
    }


    /// <summary>
    /// 把视频放进缓存目录并改回 .mp4，返回缓存中的文件名；失败返回 null
    /// </summary>
    private async Task<string?> EnsureVideoAsync(ManjuuLauncherCache? cache, string mediaFolder, CancellationToken cancellationToken)
    {
        if (ManjuuBackgroundMapper.GetVideoCacheName(cache) is not string name)
        {
            return null;
        }
        try
        {
            string path = BackgroundService.GetBgFilePath(name);
            if (File.Exists(path))
            {
                return name;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string? local = LocalFile(mediaFolder, cache!.VideoName);
            if (local is not null)
            {
                File.Copy(local, path, true);
                return name;
            }
            if (string.IsNullOrWhiteSpace(cache.VideoUrl))
            {
                return null;
            }
            byte[] bytes = await _httpClient.GetByteArrayAsync(cache.VideoUrl, cancellationToken);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return name;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache Azur Promilia background video");
            return null;
        }
    }


    private async Task<byte[]?> ReadLocalOrDownloadAsync(string mediaFolder, string? localName, string? url, CancellationToken cancellationToken)
    {
        if (LocalFile(mediaFolder, localName) is string local)
        {
            return await File.ReadAllBytesAsync(local, cancellationToken);
        }
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }
        return await _httpClient.GetByteArrayAsync(url, cancellationToken);
    }


    /// <summary>
    /// 媒体缓存里的文件，只接受单纯的文件名，不让 cache.json 指到目录外
    /// </summary>
    private static string? LocalFile(string mediaFolder, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name))
        {
            return null;
        }
        string path = Path.Join(mediaFolder, name);
        return File.Exists(path) ? path : null;
    }


    /// <summary>
    /// 裁掉透明的标题栏缺口与圆角，存成 png。不需要裁时也重新编码成 png，缓存文件名的扩展名才对得上。
    /// </summary>
    private static async Task<byte[]> CropTransparentEdgesAsync(byte[] image)
    {
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(image.AsBuffer());
        input.Seek(0);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(input);
        using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight);
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
        byte[] pixels = new byte[width * height * 4];
        bitmap.CopyToBuffer(pixels.AsBuffer());

        using var output = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap);
        if (ManjuuBackgroundMapper.GetCropRect(pixels, width, height) is var (x, y, w, h))
        {
            encoder.BitmapTransform.Bounds = new BitmapBounds { X = (uint)x, Y = (uint)y, Width = (uint)w, Height = (uint)h };
        }
        await encoder.FlushAsync();

        byte[] result = new byte[output.Size];
        output.Seek(0);
        await output.ReadAsync(result.AsBuffer(), (uint)result.Length, InputStreamOptions.None);
        return result;
    }

}
