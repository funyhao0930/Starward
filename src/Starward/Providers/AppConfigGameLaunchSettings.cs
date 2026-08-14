using Starward.Core;
using Starward.Core.Games;
using Starward.Features.GameLauncher;

namespace Starward.Providers;

/// <summary>
/// 用 <see cref="AppConfig"/> 实现启动设置的读写，适用于所有供应商。
/// <para/>
/// 键一律通过 <see cref="GameKeyResolver.ToSettingsKey"/> 计算：
/// 米哈游游戏仍是旧的 GameBiz 字符串，保证用户既有数据不失效；
/// 其他供应商是 GameKey 的正规字符串。
/// </summary>
internal class AppConfigGameLaunchSettings : IGameLaunchSettings
{

    public string? GetStartArgument(GameKey key)
    {
        return AppConfig.GetStartArgument(SettingsKey(key));
    }


    public bool GetUsePopupWindow(GameKey key)
    {
        return AppConfig.GetUsePopupWindow(SettingsKey(key));
    }


    public bool GetEnableDX12(GameKey key)
    {
        return AppConfig.GetEnableDX12(SettingsKey(key));
    }


    public bool GetEnableThirdPartyTool(GameKey key)
    {
        return AppConfig.GetEnableThirdPartyTool(SettingsKey(key));
    }


    public string? GetThirdPartyToolPath(GameKey key)
    {
        string? path = AppConfig.GetThirdPartyToolPath(SettingsKey(key));
        if (!string.IsNullOrWhiteSpace(path))
        {
            path = GameLauncherService.GetFullPathIfRelativePath(path);
        }
        return path;
    }


    public void ClearThirdPartyToolPath(GameKey key)
    {
        AppConfig.SetThirdPartyToolPath(SettingsKey(key), null);
    }


    public bool StartGameWithCommandPrompt => AppConfig.StartGameWithCMD;


    /// <summary>
    /// AppConfig 的键是纯字符串，<see cref="GameBiz"/> 只是它的包装
    /// </summary>
    private static GameBiz SettingsKey(GameKey key) => new(GameKeyResolver.ToSettingsKey(key));

}
