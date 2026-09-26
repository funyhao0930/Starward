using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Core.Launcher.Gryphline;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Gryphline;

/// <summary>
/// 搜索已安装的明日方舟：终末地。
/// <para/>
/// GRYPHLINK 启动器的卸载项键名是一段哈希，不能写死，只能按显示名称匹配。
/// 卸载项记录的是启动器的安装根目录，游戏在它下面的 games 子目录中。
/// </summary>
internal class GryphlineDiscoveryProvider : UninstallRegistryDiscoveryProvider
{

    private readonly GryphlineLauncherClient _launcherClient;


    /// <summary>
    /// 官方公布的最新游戏包，一个工作阶段内只查一次
    /// </summary>
    private GryphlineLatestGame? _latestGame;

    /// <summary>
    /// 上次查询失败的时间。失败不缓存结果，但也不能每次切页都重试。
    /// </summary>
    private DateTimeOffset _lastFailedTime = DateTimeOffset.MinValue;

    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 慢到这个地步就别拖着启动页了
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);


    public GryphlineDiscoveryProvider(ILogger<GryphlineDiscoveryProvider> logger, GryphlineLauncherClient launcherClient)
        : base(logger, new SimpleGameCatalogProvider(GryphlineGameMapping.ProviderId, GryphlineGameMapping.GetDescriptors))
    {
        _launcherClient = launcherClient;
    }


    public override string ProviderId => GryphlineGameMapping.ProviderId;


    protected override IReadOnlyList<GameKey> SupportedGameKeys => GryphlineGameMapping.SupportedGameKeys;


    protected override string? GetUninstallDisplayName(GameKey key) => GryphlineGameMapping.LauncherDisplayName;


    /// <summary>
    /// 卸载项记录的是启动器根目录，游戏的可执行文件路径已经包含 games 子目录，
    /// 因此安装目录就是启动器根目录，无需调整。
    /// </summary>
    protected override string? ResolveGameFolder(GameKey key, string installLocation) => installLocation;


    /// <summary>
    /// 终末地公布的是游戏本体的版本
    /// </summary>
    public override GameVersionSource LatestVersionSource => GameVersionSource.Game;


    public override async ValueTask<Version?> GetLatestVersionAsync(GameKey key, string installPath, CancellationToken cancellationToken = default)
    {
        GryphlineLatestGame? latest = await GetLatestGameAsync(key, cancellationToken);
        return Version.TryParse(latest?.Version, out Version? version) ? version : null;
    }


    /// <summary>
    /// 不比版本号，比安装清单的校验值，理由见 <see cref="GryphlineVersionMapper"/>。
    /// <para/>
    /// 因此也不声明 <see cref="GameCapability.VersionCheck"/>：那个能力说的是「读得到本机版本号」，
    /// 声明了而读不到，启动按钮会被当成要续传下载。
    /// </summary>
    public override async ValueTask<GameUpdateInfo?> GetUpdateInfoAsync(GameKey key, string installPath, Version? localVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }
        GryphlineLatestGame? latest = await GetLatestGameAsync(key, cancellationToken);
        if (latest is null)
        {
            return null;
        }
        return GryphlineVersionMapper.ToUpdateInfo(latest, await GetLocalGameFilesMd5Async(installPath, cancellationToken));
    }


    private async Task<GryphlineLatestGame?> GetLatestGameAsync(GameKey key, CancellationToken cancellationToken)
    {
        if (key != GryphlineGameMapping.EndfieldDefault)
        {
            return null;
        }
        if (_latestGame is not null)
        {
            return _latestGame;
        }
        if (DateTimeOffset.Now - _lastFailedTime < RetryInterval)
        {
            return null;
        }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            GryphlineLatestGame? latest = await _launcherClient.GetLatestGameAsync(GryphlineLauncherClient.ENDFIELD_APP_CODE, timeout.Token);
            if (latest is not null)
            {
                _latestGame = latest;
                return latest;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Get Endfield latest game");
        }
        _lastFailedTime = DateTimeOffset.Now;
        return null;
    }


    /// <summary>
    /// 本机安装清单的 MD5，读不到返回 null。
    /// 清单只有两百多 KB，每次进启动页算一次不必缓存。
    /// </summary>
    private async Task<string?> GetLocalGameFilesMd5Async(string installPath, CancellationToken cancellationToken)
    {
        try
        {
            string path = Path.Combine(installPath, GryphlineGameMapping.GameFolderName, GryphlineVersionMapper.GameFilesName);
            if (!File.Exists(path))
            {
                return null;
            }
            await using FileStream fs = File.OpenRead(path);
            byte[] hash = await MD5.HashDataAsync(fs, cancellationToken);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(ex, "Read Endfield game_files");
            return null;
        }
    }

}
