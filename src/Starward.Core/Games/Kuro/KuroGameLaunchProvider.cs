namespace Starward.Core.Games.Kuro;

/// <summary>
/// 鸣潮的启动。与通用实现只差一个参数：3.7.0 起必须用 <c>-krqlv=</c> 告诉游戏挂载哪一档资源，
/// 而这要看本机实际装了哪几档，不能像以前那样在游戏描述里写死 <c>-krqlv=hd</c>，
/// 否则只装了极致或流畅的玩家一启动就出错。规则见 <see cref="KuroResourceTier.DecideLaunchTier"/>。
/// </summary>
public class KuroGameLaunchProvider : SimpleGameLaunchProvider, IGameResourceTierProvider
{

    public KuroGameLaunchProvider(IGameCatalogProvider catalog, IGameLaunchSettings settings) : base(KuroGameMapping.ProviderId, catalog, settings)
    {

    }



    protected override string? BuildArguments(GameDescriptor descriptor, GameKey key, string? installPath, string? startArgument)
    {
        (_, KuroLaunchTierDecision decision) = Decide(key, installPath, startArgument);
        return JoinArguments(descriptor.LaunchArguments, decision.TierArgument, decision.StartArgument);
    }



    public GameResourceTierState GetResourceTierState(GameKey key, string installPath)
    {
        (IReadOnlyList<string> installed, KuroLaunchTierDecision decision) = Decide(key, installPath, Settings.GetStartArgument(key)?.Trim());
        return new GameResourceTierState(installed, decision.Tier);
    }



    private (IReadOnlyList<string> Installed, KuroLaunchTierDecision Decision) Decide(GameKey key, string? installPath, string? startArgument)
    {
        // 游戏本体在官方启动器安装根目录下的 Wuthering Waves Game
        string? gameDir = string.IsNullOrWhiteSpace(installPath) ? null : Path.Combine(installPath, KuroGameMapping.GameFolderName);
        IReadOnlyList<string> installed = KuroResourceTier.GetInstalledTiers(gameDir);
        return (installed, KuroResourceTier.DecideLaunchTier(installed, Settings.GetResourceTier(key), startArgument));
    }

}
