namespace Starward.Core.Games;

/// <summary>
/// 供应商目录的通用实现，适用于游戏与渠道固定、不需要在线获取清单的供应商。
/// 只支持启动的游戏用它就够了，不必各自实现 <see cref="IGameCatalogProvider"/>。
/// </summary>
public class SimpleGameCatalogProvider : IGameCatalogProvider
{

    private readonly Func<IReadOnlyList<GameDescriptor>> _descriptorFactory;


    /// <param name="providerId">供应商标识</param>
    /// <param name="descriptorFactory">
    /// 每次调用都重新生成描述，使显示名称能跟随界面语言变化
    /// </param>
    public SimpleGameCatalogProvider(string providerId, Func<IReadOnlyList<GameDescriptor>> descriptorFactory)
    {
        ProviderId = providerId;
        _descriptorFactory = descriptorFactory;
    }


    public string ProviderId { get; }


    public IReadOnlyList<GameDescriptor> GetGames() => _descriptorFactory();


    public GameDescriptor? GetGame(GameKey key)
    {
        if (!key.IsProvider(ProviderId))
        {
            return null;
        }
        return _descriptorFactory().FirstOrDefault(x => x.Key == key);
    }


    /// <summary>
    /// 清单是固定的，无需刷新
    /// </summary>
    public ValueTask RefreshAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

}
