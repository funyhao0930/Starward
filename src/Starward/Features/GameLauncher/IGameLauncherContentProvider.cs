using Starward.Core.Games;
using Starward.Core.HoYoPlay;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 提供某个供应商的启动页横幅与资讯。
/// <para/>
/// 各家官方启动器都把首页要显示的横幅、公告放在自己的接口里，形状差别很大：
/// 米哈游一支 getGameContent 就都有了，库洛是一个渠道一种语言一份静态 JSON，
/// 鹰角是一支聚合接口、横幅与公告分成两个 kind。
/// <see cref="GameBannerAndPost"/> 不应该认识这些差异，只透过本接口拿到统一的
/// <see cref="GameContent"/>。
/// <para/>
/// 定义在应用层而不是 Starward.Core，理由与 <c>IGameBackgroundProvider</c> 相同：
/// 米哈游那一路要用到应用层的 <see cref="HoYoPlay.HoYoPlayService"/>。
/// </summary>
public interface IGameLauncherContentProvider
{

    /// <summary>
    /// 供应商标识，与 <see cref="GameKey.ProviderId"/> 对应
    /// </summary>
    string ProviderId { get; }


    /// <summary>
    /// 该游戏有没有横幅与资讯接口。
    /// 同一个供应商下未必每款游戏、每个渠道都有。
    /// </summary>
    bool Supports(GameKey key);


    /// <summary>
    /// 取得横幅与资讯，没有内容时返回 null。
    /// <para/>
    /// 接口调用失败时抛出，由调用方决定要不要把这一块藏起来。
    /// </summary>
    Task<GameContent?> GetContentAsync(GameKey key, CancellationToken cancellationToken = default);

}
