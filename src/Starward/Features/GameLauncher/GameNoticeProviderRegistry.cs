using Starward.Core.Games;
using Starward.Core.Launcher;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 按供应商标识找到游戏内公告的 Provider，并记住每款游戏看过哪些公告。
/// <para/>
/// 与 <see cref="LauncherContentProviderRegistry"/> 同样的思路。
/// </summary>
public class GameNoticeProviderRegistry
{

    private readonly Dictionary<string, IGameNoticeProvider> _providers;


    public GameNoticeProviderRegistry(IEnumerable<IGameNoticeProvider> providers)
    {
        _providers = providers.ToDictionary(x => x.ProviderId, StringComparer.OrdinalIgnoreCase);
    }


    /// <summary>
    /// 该游戏有没有游戏内公告
    /// </summary>
    public bool Supports(GameKey key)
    {
        return GetProvider(key) is not null;
    }


    /// <summary>
    /// 取得公告板，该游戏没有游戏内公告时返回 null
    /// </summary>
    public async Task<GameNoticeBoard?> GetBoardAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (GetProvider(key) is not IGameNoticeProvider provider)
        {
            return null;
        }
        return await provider.GetBoardAsync(key, cancellationToken);
    }


    /// <summary>
    /// 一则公告的正文，已经随列表拿到的直接返回
    /// </summary>
    public async Task<string?> GetContentHtmlAsync(GameKey key, GameNoticeItem item, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(item.ContentHtml))
        {
            return item.ContentHtml;
        }
        if (GetProvider(key) is not IGameNoticeProvider provider)
        {
            return null;
        }
        return await provider.GetContentHtmlAsync(key, item, cancellationToken);
    }


    /// <summary>
    /// 有没有该提示红点、却还没看过的公告
    /// </summary>
    public async Task<bool> HasUnreadAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        GameNoticeBoard? board = await GetBoardAsync(key, cancellationToken);
        if (board is null)
        {
            return false;
        }
        HashSet<string> read = GetReadIds(key);
        return board.Tabs.SelectMany(x => x.Items).Any(x => IsUnread(x, read));
    }


    /// <summary>
    /// 看过的公告，没看过的且该提示红点的才算未读
    /// </summary>
    public static bool IsUnread(GameNoticeItem item, IReadOnlySet<string> readIds)
    {
        return item.NeedRedDot && !readIds.Contains(item.Id);
    }


    /// <summary>
    /// 这款游戏看过的公告
    /// </summary>
    public static HashSet<string> GetReadIds(GameKey key)
    {
        string? value = AppConfig.GetValue<string>(null, GetReadIdsSettingKey(key));
        return new HashSet<string>(value?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [], StringComparer.Ordinal);
    }


    /// <summary>
    /// 记下看过的公告。只留还在公告板上的，下架的不再记，免得越存越多。
    /// </summary>
    public static void SaveReadIds(GameKey key, IEnumerable<string> readIds, GameNoticeBoard board)
    {
        var online = new HashSet<string>(board.Tabs.SelectMany(x => x.Items).Select(x => x.Id), StringComparer.Ordinal);
        AppConfig.SetValue(string.Join('\n', readIds.Where(online.Contains).Distinct()), GetReadIdsSettingKey(key));
    }


    private static string GetReadIdsSettingKey(GameKey key) => $"game_notice_read_{key}";


    private IGameNoticeProvider? GetProvider(GameKey key)
    {
        if (!key.IsValid)
        {
            return null;
        }
        if (_providers.TryGetValue(key.ProviderId, out IGameNoticeProvider? provider) && provider.Supports(key))
        {
            return provider;
        }
        return null;
    }

}
