using Starward.Core.HoYoPlay;

namespace Starward.Core.Games.HoYo;

/// <summary>
/// 提供 HoYoPlay 接口返回的游戏信息。由应用层实现（包装 HoYoPlayService），
/// 使 <see cref="HoYoCatalogProvider"/> 不必直接依赖网络与缓存实现。
/// </summary>
public interface IHoYoGameInfoSource
{

    /// <summary>
    /// 已缓存的游戏信息，不发起网络请求
    /// </summary>
    IReadOnlyList<GameInfo> GetCachedGameInfos();


    /// <summary>
    /// 重新拉取游戏信息并更新缓存
    /// </summary>
    ValueTask RefreshAsync(CancellationToken cancellationToken = default);

}
