using Microsoft.Extensions.Caching.Memory;
using Starward.Core.Games;
using Starward.Core.Games.Kuro;
using Starward.Core.Launcher;
using Starward.Core.Launcher.Kuro;
using Starward.Features.GameLauncher;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Kuro;

/// <summary>
/// 鸣潮的游戏内公告，清单与正文都是 CDN 上的静态 JSON，正文点开时才取。
/// </summary>
internal class KuroGameNoticeProvider : IGameNoticeProvider
{

    private readonly KuroLauncherClient _client;

    private readonly IMemoryCache _memoryCache;


    public KuroGameNoticeProvider(KuroLauncherClient client, IMemoryCache memoryCache)
    {
        _client = client;
        _memoryCache = memoryCache;
    }


    public string ProviderId => KuroGameMapping.ProviderId;


    /// <summary>
    /// 公告的 serverId 是国际服的，现在只有国际服
    /// </summary>
    public bool Supports(GameKey key) => key == KuroGameMapping.WutheringWavesGlobal;


    public async Task<GameNoticeBoard?> GetBoardAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (!Supports(key))
        {
            return null;
        }
        string language = KuroLauncherClient.GetLanguageCode();
        // 启动页查红点和打开公告窗口会接连问同一份清单，留一分钟的短缓存
        string cacheKey = $"{nameof(KuroGameNoticeProvider)}_{language}";
        if (!_memoryCache.TryGetValue(cacheKey, out GameNoticeBoard? board))
        {
            KuroGameNoticeList? list = await _client.GetGameNoticeListAsync(cancellationToken);
            board = KuroNoticeMapper.ToNoticeBoard(list, language, DateTimeOffset.Now);
            if (board is not null)
            {
                _memoryCache.Set(cacheKey, board, TimeSpan.FromMinutes(1));
            }
        }
        return board;
    }


    public async Task<string?> GetContentHtmlAsync(GameKey key, GameNoticeItem item, CancellationToken cancellationToken = default)
    {
        if (!Supports(key) || item.ContentUrls.Count is 0)
        {
            return null;
        }
        string language = KuroLauncherClient.GetLanguageCode();
        string cacheKey = $"{nameof(KuroGameNoticeProvider)}_{item.Id}_{language}";
        if (!_memoryCache.TryGetValue(cacheKey, out string? html))
        {
            KuroGameNoticeContent? content = await _client.GetGameNoticeContentAsync(item.ContentUrls, language, cancellationToken);
            html = KuroNoticeMapper.ToContentHtml(content);
            if (html is not null)
            {
                _memoryCache.Set(cacheKey, html, TimeSpan.FromMinutes(10));
            }
        }
        return html;
    }

}
