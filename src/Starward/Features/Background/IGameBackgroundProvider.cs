using Starward.Core.Games;
using Starward.Core.HoYoPlay;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Background;

/// <summary>
/// 提供某个供应商的在线背景图。
/// <para/>
/// 各家官方启动器都把首页背景放在自己的 CDN 上，但形状差别很大：米哈游一支接口返回
/// 所有游戏的多张背景并带服务器端 ID，鸣潮是一个渠道一种语言一份文件、只有一张背景。
/// <see cref="BackgroundService"/> 不应该认识这些差异，只透过本接口拿到统一的
/// <see cref="GameBackground"/> 列表。
/// <para/>
/// 定义在应用层而不是 Starward.Core，因为米哈游那一路要用到应用层的
/// <see cref="HoYoPlay.HoYoPlayService"/>（它管着缓存与多启动器合并）。
/// </summary>
public interface IGameBackgroundProvider
{

    /// <summary>
    /// 供应商标识，与 <see cref="GameKey.ProviderId"/> 对应
    /// </summary>
    string ProviderId { get; }


    /// <summary>
    /// 该游戏有没有在线背景图接口。
    /// 同一个供应商下未必每款游戏、每个渠道都有。
    /// </summary>
    bool Supports(GameKey key);


    /// <summary>
    /// 取得在线背景图，排在前面的是最新的一张。
    /// <para/>
    /// 不支持该游戏时返回空列表；接口调用失败时抛出，由调用方决定要不要退回本机图。
    /// </summary>
    Task<List<GameBackground>> GetBackgroundsAsync(GameKey key, CancellationToken cancellationToken = default);

}
