namespace Starward.Core.Games;

/// <summary>
/// 提供某个供应商支持的游戏与渠道清单，以及显示名称、图标等基本资料。
/// <para/>
/// 读取是同步的，实现应当只使用本地缓存；需要网络的部分放在 <see cref="RefreshAsync"/> 中。
/// </summary>
public interface IGameCatalogProvider : IGameProvider
{

    /// <summary>
    /// 支持的所有游戏与渠道
    /// </summary>
    IReadOnlyList<GameDescriptor> GetGames();


    /// <summary>
    /// 指定游戏的描述，不支持时返回 null
    /// </summary>
    GameDescriptor? GetGame(GameKey key);


    /// <summary>
    /// 刷新清单，例如重新拉取供应商的在线数据。实现可以是空操作。
    /// </summary>
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);

}
