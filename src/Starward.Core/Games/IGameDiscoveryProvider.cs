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

}
