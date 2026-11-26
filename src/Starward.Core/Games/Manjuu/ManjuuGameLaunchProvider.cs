namespace Starward.Core.Games.Manjuu;

/// <summary>
/// 蓝色星原：旅谣的启动。
/// <para/>
/// 命令本身与通用实现一样，差别只在追踪：<c>AzurPromilia.exe</c> 一开就退出，
/// 再以系统管理员身份拉起一个同名的进程（实测进程号会变）。
/// 通用实现在进程名与文件名相同时直接追踪启动的那个进程，
/// 那样游玩时间会在几秒后就结束，因此这里一律改按进程名找。
/// </summary>
public class ManjuuGameLaunchProvider : SimpleGameLaunchProvider
{

    public ManjuuGameLaunchProvider(IGameCatalogProvider catalog, IGameLaunchSettings settings) : base(ManjuuGameMapping.ProviderId, catalog, settings)
    {

    }


    public override async ValueTask<GameLaunchCommand> CreateLaunchCommandAsync(GameKey key, GameLaunchOptions options, CancellationToken cancellationToken = default)
    {
        GameLaunchCommand command = await base.CreateLaunchCommandAsync(key, options, cancellationToken);
        return command with { TrackByProcessName = true };
    }

}
