using Starward.Core.Games;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameInstall;

/// <summary>
/// 非米哈游游戏的安装包信息，供启动页、安装对话框与预下载对话框使用。
/// <para/>
/// 米哈游游戏走 <see cref="HoYoPlay.HoYoPlayService"/>，与这里无关。
/// 真正的下载在 RPC 进程里由各家的 <c>IGameInstallVendor</c> 完成，
/// 这里只回答界面要显示的东西：多大、要不要更新、能不能预下载。
/// </summary>
public interface IGamePackageInfoProvider
{

    /// <summary>
    /// 对应 <see cref="GameKey.ProviderId"/>
    /// </summary>
    string ProviderId { get; }


    bool Supports(GameKey key);


    /// <summary>
    /// 在用户选择的父目录下自动创建的子目录名，与官方启动器的默认目录名一致
    /// </summary>
    string GetDefaultFolderName(GameKey key);


    /// <summary>
    /// 全新安装要下载多少、装完占多少，查不到时返回 null
    /// </summary>
    Task<GamePackageSize?> GetInstallSizeAsync(GameKey key, CancellationToken cancellationToken = default);


    /// <summary>
    /// 本机安装的状态：要不要更新、能不能预下载、预下载是否已完成。查不到时返回 null。
    /// </summary>
    /// <param name="installPath">Starward 记录的安装路径</param>
    Task<GamePackageState?> GetStateAsync(GameKey key, string installPath, CancellationToken cancellationToken = default);

}


/// <summary>
/// 安装包大小
/// </summary>
/// <param name="Version">线上版本</param>
/// <param name="DownloadBytes">要下载的字节数</param>
/// <param name="InstallBytes">装完占用的字节数，也是至少要有的剩余空间</param>
public sealed record GamePackageSize(string Version, long DownloadBytes, long InstallBytes);


/// <summary>
/// 本机安装相对于线上版本的状态
/// </summary>
public sealed record GamePackageState
{

    /// <summary>
    /// 本机版本，读不到时为 null（终末地的版本文件是加密的）
    /// </summary>
    public string? LocalVersion { get; init; }

    /// <summary>
    /// 线上版本
    /// </summary>
    public string? LatestVersion { get; init; }

    /// <summary>
    /// 本机是否落后于线上版本
    /// </summary>
    public bool UpdateAvailable { get; init; }

    /// <summary>
    /// 可以预下载时是预下载的版本号，否则为 null
    /// </summary>
    public string? PredownloadVersion { get; init; }

    /// <summary>
    /// 预下载要下载的字节数
    /// </summary>
    public long PredownloadBytes { get; init; }

    /// <summary>
    /// 预下载已经完成
    /// </summary>
    public bool PredownloadFinished { get; init; }

}
