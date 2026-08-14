namespace Starward.Core.Games;

public static class GameProviderRegistryExtensions
{

    /// <summary>
    /// 指定游戏是否支持某项能力。
    /// <para/>
    /// 调用只有部分供应商才有的接口前用它判断，避免做无谓的工作、
    /// 也避免在日志里留下一串本可预期的异常。
    /// 找不到描述时返回 true，让调用方按原有流程走，由服务自己守门。
    /// </summary>
    public static bool SupportsCapability(this IGameProviderRegistry registry, GameKey key, GameCapability capability)
    {
        if (!key.IsValid)
        {
            return false;
        }
        return registry.GetGame(key)?.HasCapability(capability) ?? true;
    }

}
