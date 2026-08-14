using Starward.Core;
using Starward.Core.Games;
using Starward.Core.Games.HoYo;
using Starward.Core.HoYoPlay;

namespace Starward.Providers.HoYo;

/// <summary>
/// <see cref="GameKey"/> 与 HoYoPlay 的 <see cref="GameId"/> 之间的唯一转换点。
/// <para/>
/// 通用代码一律使用 <see cref="GameKey"/>，只有真正要调用 HoYoPlay 接口时才在
/// 边界上换成 <see cref="GameId"/>。非米哈游的游戏返回 null。
/// </summary>
internal static class HoYoGameIds
{

    /// <summary>
    /// 崩坏三国际服的区服，由用户在启动页选择，保存在应用配置中。
    /// </summary>
    private static string? SelectedBH3GlobalGameId => AppConfig.LastGameIdOfBH3Global;


    /// <summary>
    /// 解析出 HoYoPlay 的 <see cref="GameId"/>，不属于米哈游时返回 null。
    /// </summary>
    public static GameId? Resolve(GameKey key)
    {
        if (!HoYoGameMapping.TryToGameBiz(key, out GameBiz gameBiz))
        {
            return null;
        }
        return Resolve(gameBiz);
    }


    /// <summary>
    /// 解析出 HoYoPlay 的 <see cref="GameId"/>，无法识别时返回 null。
    /// </summary>
    public static GameId? Resolve(GameBiz gameBiz)
    {
        if (GameId.FromGameBiz(gameBiz) is not GameId gameId)
        {
            return null;
        }
        // 崩坏三国际服有多个区服，各自是不同的远程 GameId。
        // 以前的做法是就地改写共用的 GameId 对象，那让身份变成了可变状态；
        // 现在每次解析时读取用户的选择，调用方拿到的始终是新对象。
        if (gameBiz == GameBiz.bh3_global && SelectedBH3GlobalGameId is string id && !string.IsNullOrWhiteSpace(id))
        {
            return new GameId { Id = id, GameBiz = gameId.GameBiz };
        }
        return gameId;
    }

}
