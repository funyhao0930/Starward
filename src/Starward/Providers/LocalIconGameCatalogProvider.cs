using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers;

/// <summary>
/// 装饰一个目录 Provider，用已安装游戏可执行文件中的图标补上缺失的游戏图标。
/// <para/>
/// 这样不必把各游戏公司的美术资源复制进代码仓库，图标也始终与实际安装的版本一致；
/// 未来新增的游戏只要不指定图标就会自动套用。
/// </summary>
internal class LocalIconGameCatalogProvider : IGameCatalogProvider
{

    /// <summary>
    /// 没有内置图标时使用的占位图
    /// </summary>
    private const string PlaceholderIconUri = "ms-appx:///Assets/Image/Transparent.png";


    private readonly IGameCatalogProvider _inner;

    private readonly ILogger _logger;


    public LocalIconGameCatalogProvider(IGameCatalogProvider inner, ILogger logger)
    {
        _inner = inner;
        _logger = logger;
    }


    public string ProviderId => _inner.ProviderId;


    public IReadOnlyList<GameDescriptor> GetGames()
    {
        return _inner.GetGames().Select(WithLocalIcon).ToList().AsReadOnly();
    }


    public GameDescriptor? GetGame(GameKey key)
    {
        return _inner.GetGame(key) is GameDescriptor descriptor ? WithLocalIcon(descriptor) : null;
    }


    public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        return _inner.RefreshAsync(cancellationToken);
    }



    private GameDescriptor WithLocalIcon(GameDescriptor descriptor)
    {
        // 已经有真正的图标就不动
        if (!string.IsNullOrWhiteSpace(descriptor.IconUri) && descriptor.IconUri != PlaceholderIconUri)
        {
            return descriptor;
        }
        return TryGetLocalIconUri(descriptor) is string uri ? descriptor with { IconUri = uri } : descriptor;
    }


    private string? TryGetLocalIconUri(GameDescriptor descriptor)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(descriptor.ExecutableName) || AppConfig.CacheFolder is null)
            {
                return null;
            }
            // 游戏没有安装时无从提取
            if (ProviderInstallPathStore.GetInstallPath(descriptor) is not string installPath)
            {
                return null;
            }
            string exe = Path.Combine(installPath, descriptor.ExecutableName);
            if (!File.Exists(exe))
            {
                return null;
            }

            string fileName = string.Concat(descriptor.SettingsKey.Split(Path.GetInvalidFileNameChars())) + ".ico";
            string cachePath = Path.Combine(AppConfig.CacheFolder, "gameicon", fileName);

            // 游戏更新后可执行文件会变新，此时重新提取
            if (!File.Exists(cachePath) || File.GetLastWriteTimeUtc(cachePath) < File.GetLastWriteTimeUtc(exe))
            {
                if (!ExecutableIconExtractor.TryExtract(exe, cachePath))
                {
                    return null;
                }
            }
            return new Uri(cachePath).AbsoluteUri;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Extract game icon: {key}", descriptor.Key);
            return null;
        }
    }

}
