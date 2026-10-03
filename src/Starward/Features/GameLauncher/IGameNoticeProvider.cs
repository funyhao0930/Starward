using Starward.Core.Games;
using Starward.Core.Launcher;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 提供某个供应商的游戏内公告。
/// <para/>
/// 米哈游的游戏内公告有官方网页，由 <see cref="GameNoticeWindow"/> 直接嵌入；
/// 其他几家在游戏里是自己画的界面，只有数据接口：库洛是静态 JSON、正文按语言另放，
/// 鹰角是一支连正文一起返回的接口，完美世界只能借用官网文章。
/// <see cref="VendorNoticeWindow"/> 透过本接口拿到统一的 <see cref="GameNoticeBoard"/>，不认识这些差异。
/// </summary>
public interface IGameNoticeProvider
{

    /// <summary>
    /// 供应商标识，与 <see cref="GameKey.ProviderId"/> 对应
    /// </summary>
    string ProviderId { get; }


    /// <summary>
    /// 该游戏有没有游戏内公告
    /// </summary>
    bool Supports(GameKey key);


    /// <summary>
    /// 取得公告板，没有内容时返回 null。接口调用失败时抛出。
    /// </summary>
    Task<GameNoticeBoard?> GetBoardAsync(GameKey key, CancellationToken cancellationToken = default);


    /// <summary>
    /// 取得 <see cref="GameNoticeItem.ContentHtml"/> 为空的公告的正文，取不到时返回 null。接口调用失败时抛出。
    /// </summary>
    Task<string?> GetContentHtmlAsync(GameKey key, GameNoticeItem item, CancellationToken cancellationToken = default);

}
