using Starward.Core.Games;

namespace Starward.Features.Gacha;

/// <summary>
/// 提供某个供应商的抽卡记录服务。
/// <para/>
/// 各家的抽卡协议差别很大：授权 URL 藏在哪个本地文件、用什么接口分页、
/// 记录有没有服务器端 ID，都不一样。抽卡页面不应该认识这些差异，
/// 只透过本接口取得对应的服务。
/// <para/>
/// 定义在应用层而不是 Starward.Core，因为 <see cref="GachaLogService"/>
/// 依赖应用的数据库与配置。
/// </summary>
internal interface IGameGachaProvider
{

    /// <summary>
    /// 供应商标识，与 <see cref="GameKey.ProviderId"/> 对应
    /// </summary>
    string ProviderId { get; }


    /// <summary>
    /// 指定游戏的记录服务，不支持时返回 null
    /// </summary>
    GachaLogService? GetService(GameKey key);

}
