using Starward.Core.Games.HoYo;

namespace Starward.Core.Games;

/// <summary>
/// 在旧的 GameBiz 字符串与通用 <see cref="GameKey"/> 之间转换。
/// <para/>
/// 应用配置、数据库与页面导航仍以字符串为键：米哈游游戏是 <c>hk4e_cn</c>，
/// 其他供应商是 GameKey 的正规字符串 <c>kuro:wuwa:global</c>。
/// 本类型负责识别两种格式，使旧数据继续可用，新游戏也能共用同一套存储。
/// </summary>
public static class GameKeyResolver
{

    /// <summary>
    /// 由存储键解析出 <see cref="GameKey"/>
    /// </summary>
    public static bool TryResolve(string? settingsKey, out GameKey key)
    {
        // 正规形式 provider:game:channel
        if (GameKey.TryParse(settingsKey, out key))
        {
            return true;
        }
        // 旧的 GameBiz 形式 game_server
        return HoYoGameMapping.TryFromGameBiz(new GameBiz(settingsKey), out key);
    }


    /// <summary>
    /// 由存储键解析出 <see cref="GameKey"/>，无法识别时返回 null
    /// </summary>
    public static GameKey? Resolve(string? settingsKey)
    {
        return TryResolve(settingsKey, out GameKey key) ? key : null;
    }


    /// <summary>
    /// 由 <see cref="GameKey"/> 得到应用配置与数据库使用的键。
    /// 这是 <see cref="GameDescriptor.SettingsKey"/> 的唯一定义，
    /// 拿不到描述对象的地方（例如读写启动设置时）也必须用它，
    /// 否则非米哈游游戏会算出不同的键，导致设置读不到。
    /// </summary>
    public static string ToSettingsKey(GameKey key)
    {
        // 米哈游游戏沿用旧的 GameBiz 字符串，保证既有数据不失效
        return HoYoGameMapping.TryToGameBiz(key, out GameBiz gameBiz) ? gameBiz.Value : key.ToString();
    }

}
