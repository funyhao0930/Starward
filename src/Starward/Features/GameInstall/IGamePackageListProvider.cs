using Starward.Core.Games;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameInstall;

/// <summary>
/// 游戏设置里「游戏资源包」那一页要列的线上资源包，米哈游以外的游戏用。
/// <para/>
/// 与 <see cref="IGamePackageInfoProvider"/> 分开：这一页只说官方 CDN 上有什么，
/// 不代表 Starward 能安装它。异环的资源由官方外壳下载，Starward 只读得到公开的版本配置。
/// </summary>
public interface IGamePackageListProvider
{

    /// <summary>
    /// 对应 <see cref="GameKey.ProviderId"/>
    /// </summary>
    string ProviderId { get; }


    bool Supports(GameKey key);


    /// <summary>
    /// 线上的资源包，查不到时返回 null
    /// </summary>
    /// <param name="installPath">Starward 记录的安装路径，没有时为 null。异环要从本机的配置读出资源地址，没装就查不到</param>
    Task<GamePackageList?> GetPackageListAsync(GameKey key, string? installPath, CancellationToken cancellationToken = default);

}


/// <summary>
/// 线上的资源包：最新版本，以及有预下载时的预下载版本
/// </summary>
public sealed record GamePackageList
{

    public required string LatestVersion { get; init; }

    public required IReadOnlyList<GamePackageGroup> Latest { get; init; }

    /// <summary>
    /// 没有预下载时为 null
    /// </summary>
    public string? PredownloadVersion { get; init; }

    public IReadOnlyList<GamePackageGroup> Predownload { get; init; } = [];

}


/// <summary>
/// 一组资源包
/// </summary>
/// <param name="FromVersion">差分包是从哪个版本更新上来的，完整包为 null</param>
/// <param name="Name">组名，既不是完整包也不是差分包时给（例如异环按需下载的附加资源）；为 null 时按 <paramref name="FromVersion"/> 显示</param>
public sealed record GamePackageGroup(IReadOnlyList<GamePackageEntry> Files, string? FromVersion = null, string? Name = null);


/// <summary>
/// 一个资源包
/// </summary>
/// <param name="Name">显示的名称：有整包文件的是文件名，鸣潮这种逐个文件下载的是资源包名</param>
/// <param name="Size">要下载的字节数</param>
/// <param name="Md5">校验值，没有时为 null</param>
/// <param name="Url">下载地址，没有公开地址时为 null，界面就不给复制</param>
public sealed record GamePackageEntry(string Name, long Size, string? Md5 = null, string? Url = null);
