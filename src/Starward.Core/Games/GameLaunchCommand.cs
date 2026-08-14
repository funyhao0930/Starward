namespace Starward.Core.Games;

/// <summary>
/// 由 <see cref="IGameLaunchProvider"/> 构建的启动命令。
/// 实际创建进程、记录游玩时间、处理错误由通用的启动服务负责。
/// </summary>
public sealed record GameLaunchCommand
{

    /// <summary>
    /// 要执行的文件，完整路径。可能是游戏本体、第三方工具或 cmd.exe。
    /// </summary>
    public required string FileName { get; init; }


    /// <summary>
    /// 命令行参数
    /// </summary>
    public string? Arguments { get; init; }


    /// <summary>
    /// 工作目录
    /// </summary>
    public string? WorkingDirectory { get; init; }


    /// <summary>
    /// ShellExecute 谓词，"runas" 表示请求管理员权限
    /// </summary>
    public string? Verb { get; init; }


    /// <summary>
    /// 使用 ShellExecute
    /// </summary>
    public bool UseShellExecute { get; init; } = true;


    /// <summary>
    /// 创建出来的进程不是游戏进程本身（第三方工具或 cmd.exe 中转），
    /// 需要按进程名查找真正的游戏进程来记录游玩时间。
    /// </summary>
    public bool TrackByProcessName { get; init; }

}
