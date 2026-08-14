namespace Starward.Core.Games;

/// <summary>
/// 提供游戏进程名并构建启动命令。
/// 实际创建进程由通用的启动服务负责，供应商只负责游戏专属的部分：
/// 进程名、启动参数、启动前操作。
/// </summary>
public interface IGameLaunchProvider : IGameProvider
{

    /// <summary>
    /// 游戏进程名，带 .exe 扩展名。无法确定时返回 null。
    /// </summary>
    ValueTask<string?> GetExecutableNameAsync(GameKey key, CancellationToken cancellationToken = default);


    /// <summary>
    /// 构建启动命令，并执行游戏专属的启动前操作。
    /// </summary>
    /// <exception cref="FileNotFoundException">游戏可执行文件不存在</exception>
    ValueTask<GameLaunchCommand> CreateLaunchCommandAsync(GameKey key, GameLaunchOptions options, CancellationToken cancellationToken = default);

}
