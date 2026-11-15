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
            board = await GetFeedBoardAsync(language, cancellationToken);
            if (board is null)
            {
                // 3.8 以前的旧清单还在，新数据取不到时至少还有公告与资讯
                KuroGameNoticeList? list = await _client.GetGameNoticeListAsync(cancellationToken);
                board = KuroNoticeMapper.ToNoticeBoard(list, language, DateTimeOffset.Now);
            }
            if (board is not null)
            {
                _memoryCache.Set(cacheKey, board, TimeSpan.FromMinutes(1));
            }
        }
        return board;
    }


    /// <summary>
    /// 3.8 起公告板网页读的那份，推荐与日历都在里面。取不到时返回 null，交给旧清单兜底。
    /// </summary>
    private async Task<GameNoticeBoard?> GetFeedBoardAsync(string language, CancellationToken cancellationToken)
    {
        try
        {
            KuroGameNoticeFeed? feed = await _client.GetGameNoticeFeedAsync(language, cancellationToken);
            return KuroNoticeMapper.ToNoticeBoard(feed, DateTimeOffset.Now);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }


    public async Task<string?> GetContentHtmlAsync(GameKey key, GameNoticeItem item, CancellationToken cancellationToken = default)
    {
        if (!Supports(key))
        {
            return null;
        }
        return KuroNoticeMapper.ToContentHtml(await GetContentAsync(item, cancellationToken));
    }


    /// <summary>
    /// 一则公告的正文，连同正文上方的横幅。<see cref="KuroNoticeWindow"/> 要分开排版，所以不只给 HTML。
    /// </summary>
    public async Task<KuroGameNoticeContent?> GetContentAsync(GameNoticeItem item, CancellationToken cancellationToken = default)
    {
        // 新数据的正文随清单一起来了
        if (item.ContentHtml is not null)
        {
            return new KuroGameNoticeContent { TextContent = item.ContentHtml, Banner = item.BannerUrl };
        }
        if (item.ContentUrls.Count is 0)
        {
            return null;
        }
        string language = KuroLauncherClient.GetLanguageCode();
        string cacheKey = $"{nameof(KuroGameNoticeProvider)}_{item.Id}_{language}";
        if (!_memoryCache.TryGetValue(cacheKey, out KuroGameNoticeContent? content))
        {
            content = await _client.GetGameNoticeContentAsync(item.ContentUrls, language, cancellationToken);
            if (content is not null)
            {
                _memoryCache.Set(cacheKey, content, TimeSpan.FromMinutes(10));
            }
        }
        return content;
    }

}
