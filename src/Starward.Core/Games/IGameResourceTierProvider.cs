namespace Starward.Core.Games;

/// <summary>
/// 同一个版本的游戏分成几档资源、启动时要挑一档的游戏（鸣潮 3.7.0 起的「用户端资源分级」）。
/// <para/>
/// 由启动 Provider 实现：启动页据此决定要不要显示分级选单，不必认得是哪款游戏。
/// 选好的分级存在 <see cref="IGameLaunchSettings.GetResourceTier"/>。
/// </summary>
public interface IGameResourceTierProvider
{

    /// <summary>
    /// 本机装好的分级，以及照现在的设置启动会用哪一档
    /// </summary>
    /// <param name="installPath">游戏安装目录</param>
    GameResourceTierState GetResourceTierState(GameKey key, string installPath);

}



/// <param name="InstalledTiers">本机装好的分级，画质从高到低</param>
/// <param name="LaunchTier">照现在的设置启动会用的分级</param>
public sealed record GameResourceTierState(IReadOnlyList<string> InstalledTiers, string LaunchTier);
