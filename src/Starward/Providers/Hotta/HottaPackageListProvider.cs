using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using Starward.Core.Games.Hotta;
using Starward.Core.Launcher.Hotta;
using Starward.Features.GameInstall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Hotta;

/// <summary>
/// 异环游戏本体的线上资源，取自 PatcherSDK 公开的版本配置，见 <see cref="HottaPatcherConfig"/>。
/// <para/>
/// 只有版本与大小：再往下的文件清单是加密的，没有可以复制的下载地址。
/// 资源由官方外壳下载，Starward 不安装异环，这里只是让游戏设置里的那一页说得出线上是哪一版。
/// 资源地址与分支都读本机的配置，没装就查不到。
/// </summary>
internal class HottaPackageListProvider : IGamePackageListProvider
{

    /// <summary>
    /// 版本配置只有几 KB，慢到这个地步就别让对话框一直空着
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);


    private readonly ILogger<HottaPackageListProvider> _logger;

    private readonly HottaLauncherClient _client;


    public HottaPackageListProvider(ILogger<HottaPackageListProvider> logger, HottaLauncherClient client)
    {
        _logger = logger;
        _client = client;
    }


    public string ProviderId => HottaGameMapping.ProviderId;


    public bool Supports(GameKey key) => key == HottaGameMapping.NevernessToEvernessTaiwan;


    public async Task<GamePackageList?> GetPackageListAsync(GameKey key, string? installPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }
        try
        {
            if (await GetSettingsAsync(installPath, cancellationToken) is not HottaPatcherSettings settings)
            {
                return null;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            if (await _client.GetResourceVersionAsync(HottaPatcherConfig.GetVersionConfigUrls(settings), timeout.Token) is not HottaResourceVersion version)
            {
                return null;
            }
            List<GamePackageEntry> files = version.Tags.Select(x => new GamePackageEntry(GetTagName(x), x.Size)).ToList();
            return new GamePackageList
            {
                LatestVersion = version.Version,
                Latest = [new GamePackageGroup(files)],
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Get Neverness to Everness resource version");
            return null;
        }
    }


    /// <summary>
    /// 外壳 Config.ini 指出 PatcherSDK 的配置目录，那里的 PatcherConfig.json 给出资源地址与分支
    /// </summary>
    private static async Task<HottaPatcherSettings?> GetSettingsAsync(string installPath, CancellationToken cancellationToken)
    {
        string configIni = Path.Combine(installPath, HottaGameMapping.ConfigFileRelativePath);
        if (!File.Exists(configIni))
        {
            return null;
        }
        if (HottaPatcherConfig.ParseConfigPath(await File.ReadAllTextAsync(configIni, cancellationToken)) is not string configPath)
        {
            return null;
        }
        string folder = configPath.Trim('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        string settingsPath = Path.Combine(installPath, HottaGameMapping.GameFolderName, folder, HottaPatcherConfig.SettingsFileName);
        if (!File.Exists(settingsPath))
        {
            return null;
        }
        return HottaPatcherConfig.ParseSettings(await File.ReadAllTextAsync(settingsPath, cancellationToken));
    }


    /// <summary>
    /// 本体叫「基础资源」，版本就是标题上的那个。其余标签（pakchunk101 这类）由游戏自己按需下载，
    /// 官方没有给名称，照原样显示，后面带上它自己的版本
    /// </summary>
    private static string GetTagName(HottaResourceTag tag)
    {
        if (tag.IsBase)
        {
            return Lang.GameResourcePage_BaseResources;
        }
        return string.IsNullOrWhiteSpace(tag.Version) ? tag.Tag : $"{tag.Tag}  {tag.Version}";
    }

}
