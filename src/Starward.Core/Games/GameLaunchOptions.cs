namespace Starward.Core.Games;

/// <summary>
/// 启动游戏时由调用方提供的上下文。
/// 用户设置（启动参数、无边框、DX12、第三方工具等）由 <see cref="IGameLaunchSettings"/> 提供，
/// 不在此重复传递。
/// </summary>
public sealed record GameLaunchOptions
{

    /// <summary>
    /// 调用方指定的游戏安装目录，优先使用。为空时使用 <see cref="ConfiguredInstallPath"/>。
    /// </summary>
    public string? InstallPath { get; init; }


    /// <summary>
    /// 应用配置中记录的游戏安装目录
    /// </summary>
    public string? ConfiguredInstallPath { get; init; }

}
