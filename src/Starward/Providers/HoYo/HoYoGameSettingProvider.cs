using Starward.Core;
using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Features.GameSetting;

namespace Starward.Providers.HoYo;

/// <summary>
/// 米哈游游戏的画面设置，存在注册表里各自的 JSON 结构中。
/// 这里只把既有的实现接到供应商接缝上，行为不变。
/// </summary>
internal class HoYoGameSettingProvider : IGameSettingProvider
{

    public string ProviderId => HoYoGameMapping.ProviderId;


    public GameResolutionSetting? GetResolution(GameKey key)
    {
        if (!HoYoGameMapping.TryToGameBiz(key, out GameBiz biz))
        {
            return null;
        }
        GraphicsSettings_PCResolution_h431323223? setting = GameSettingService.GetGameResolutionSetting(biz);
        if (setting is null)
        {
            return null;
        }
        return new GameResolutionSetting(setting.Width, setting.Height, setting.IsFullScreen);
    }


    public void SetResolution(GameKey key, GameResolutionSetting setting)
    {
        if (!HoYoGameMapping.TryToGameBiz(key, out GameBiz biz))
        {
            return;
        }
        GameSettingService.SetGameResolutionSetting(biz, new GraphicsSettings_PCResolution_h431323223
        {
            Width = setting.Width,
            Height = setting.Height,
            IsFullScreen = setting.FullScreen,
        });
    }

}
