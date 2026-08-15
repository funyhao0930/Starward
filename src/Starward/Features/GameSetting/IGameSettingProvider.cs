using Starward.Core.Games;

namespace Starward.Features.GameSetting;

/// <summary>
/// 读写某个供应商游戏的画面设置。
/// <para/>
/// 每家存的地方都不一样：米哈游在注册表里放自家的 JSON，虚幻引擎的游戏
/// 用 GameUserSettings.ini，Unity 的游戏用引擎自己的注册表键。设置页面
/// 不应该认识这些差异，只透过本接口读写。
/// </summary>
internal interface IGameSettingProvider
{

    /// <summary>
    /// 供应商标识，与 <see cref="GameKey.ProviderId"/> 对应
    /// </summary>
    string ProviderId { get; }


    /// <summary>
    /// 读取分辨率与窗口模式，读不到返回 null
    /// </summary>
    GameResolutionSetting? GetResolution(GameKey key);


    /// <summary>
    /// 写入分辨率与窗口模式
    /// </summary>
    void SetResolution(GameKey key, GameResolutionSetting setting);

}
