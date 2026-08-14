namespace Starward.Core.Games.HoYo;

/// <summary>
/// 米哈游游戏启动过程中需要应用层协助的操作。
/// 由应用层实现，使 <see cref="HoYoLaunchProvider"/> 不必依赖 WinUI 与注册表。
/// </summary>
public interface IHoYoLaunchHooks
{

    /// <summary>
    /// 创建登录认证票据，未启用或失败时返回 null
    /// </summary>
    ValueTask<string?> CreateAuthTicketAsync(GameKey key, CancellationToken cancellationToken = default);


    /// <summary>
    /// 游戏专属的启动前操作，例如原神的 HDR 设置
    /// </summary>
    ValueTask ApplyPreLaunchSettingsAsync(GameKey key, CancellationToken cancellationToken = default);


    /// <summary>
    /// 通过 HoYoPlay 接口查询游戏进程名，用于静态表中没有的游戏
    /// </summary>
    ValueTask<string?> GetRemoteExecutableNameAsync(GameKey key, CancellationToken cancellationToken = default);

}
