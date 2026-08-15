namespace Starward.Core.Games;

/// <summary>
/// 搜索已安装的游戏并返回安装路径。
/// 实现可以从注册表、配置文件或官方启动器的数据中获取。
/// </summary>
public interface IGameDiscoveryProvider : IGameProvider
{

    /// <summary>
    /// 指定游戏的安装信息，未安装或找不到时返回 null
    /// </summary>
    ValueTask<GameInstallation?> GetInstallationAsync(GameKey key, CancellationToken cancellationToken = default);


    /// <summary>
    /// 搜索该供应商所有已安装的游戏
    /// </summary>
    ValueTask<IReadOnlyList<GameInstallation>> DiscoverAsync(CancellationToken cancellationToken = default);


    /// <summary>
    /// 读取本地已安装的版本号。每家记录版本的方式都不同，
    /// 返回 null 表示无法确定，由调用方决定如何处理。
    /// </summary>
    ValueTask<Version?> GetLocalVersionAsync(GameKey key, string installPath, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<Version?>(null);
    }


    /// <summary>
    /// 查询厂商公布的最新版本号，返回 null 表示这款游戏没有可用的版本来源。
    /// <para/>
    /// 与安装、更新无关：有下载器的游戏走各自的下载接口，这里是给
    /// 只支持启动的游戏用的，只为了告诉玩家「该去官方启动器更新了」。
    /// 有没有这个来源由实现与否决定，不另设能力标志。
    /// </summary>
    ValueTask<Version?> GetLatestVersionAsync(GameKey key, string installPath, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<Version?>(null);
    }

}
