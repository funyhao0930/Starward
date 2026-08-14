using Starward.Core;
using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Features.GameLauncher;

namespace Starward.Providers.HoYo;

/// <summary>
/// 用 <see cref="AppConfig"/> 实现启动设置的读写。
/// 所有配置项仍以旧的 GameBiz 字符串作为键，保证用户既有数据不失效。
/// </summary>
internal class AppConfigGameLaunchSettings : IGameLaunchSettings
{

    public string? GetStartArgument(GameKey key)
    {
        return TryGetGameBiz(key, out GameBiz biz) ? AppConfig.GetStartArgument(biz) : null;
    }


    public bool GetUsePopupWindow(GameKey key)
    {
        return TryGetGameBiz(key, out GameBiz biz) && AppConfig.GetUsePopupWindow(biz);
    }


    public bool GetEnableDX12(GameKey key)
    {
        return TryGetGameBiz(key, out GameBiz biz) && AppConfig.GetEnableDX12(biz);
    }


    public bool GetEnableThirdPartyTool(GameKey key)
    {
        return TryGetGameBiz(key, out GameBiz biz) && AppConfig.GetEnableThirdPartyTool(biz);
    }


    public string? GetThirdPartyToolPath(GameKey key)
    {
        if (!TryGetGameBiz(key, out GameBiz biz))
        {
            return null;
        }
        string? path = AppConfig.GetThirdPartyToolPath(biz);
        if (!string.IsNullOrWhiteSpace(path))
        {
            path = GameLauncherService.GetFullPathIfRelativePath(path);
        }
        return path;
    }


    public void ClearThirdPartyToolPath(GameKey key)
    {
        if (TryGetGameBiz(key, out GameBiz biz))
        {
            AppConfig.SetThirdPartyToolPath(biz, null);
        }
    }


    public bool StartGameWithCommandPrompt => AppConfig.StartGameWithCMD;


    private static bool TryGetGameBiz(GameKey key, out GameBiz gameBiz)
    {
        return HoYoGameMapping.TryToGameBiz(key, out gameBiz);
    }

}
