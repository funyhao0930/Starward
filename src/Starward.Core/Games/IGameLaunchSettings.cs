namespace Starward.Core.Games;

/// <summary>
/// 启动游戏时需要的用户设置。由应用层实现（读写 AppConfig），
/// 使 <see cref="IGameLaunchProvider"/> 的实现不必依赖应用层的配置存储。
/// </summary>
public interface IGameLaunchSettings
{

    /// <summary>
    /// 自定义启动参数
    /// </summary>
    string? GetStartArgument(GameKey key);


    /// <summary>
    /// 无边框窗口
    /// </summary>
    bool GetUsePopupWindow(GameKey key);


    /// <summary>
    /// 使用 DirectX 12
    /// </summary>
    bool GetEnableDX12(GameKey key);


    /// <summary>
    /// 启用第三方工具启动
    /// </summary>
    bool GetEnableThirdPartyTool(GameKey key);


    /// <summary>
    /// 第三方工具路径，文件不存在时返回 null
    /// </summary>
    string? GetThirdPartyToolPath(GameKey key);


    /// <summary>
    /// 第三方工具找不到时清除已保存的路径
    /// </summary>
    void ClearThirdPartyToolPath(GameKey key);


    /// <summary>
    /// 通过 cmd.exe 启动游戏
    /// </summary>
    bool StartGameWithCommandPrompt { get; }

}
