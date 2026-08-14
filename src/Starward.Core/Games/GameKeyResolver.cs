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

}
