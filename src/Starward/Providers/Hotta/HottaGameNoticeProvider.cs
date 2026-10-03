using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using Starward.Core.Games.Hotta;
using Starward.Core.Launcher;
using Starward.Core.Launcher.Hotta;
using Starward.Features.GameLauncher;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Hotta;

/// <summary>
/// 异环的公告。
/// <para/>
/// 游戏内那块公告由游戏服务器下发，本机日志也是加密的，找不到公开的数据来源，
/// 因此用台服官网「情报速递」的系统、活动、新闻三个分页代替，正文点开时到文章页取。
/// </summary>
internal class HottaGameNoticeProvider : IGameNoticeProvider
{

    private readonly ILogger<HottaGameNoticeProvider> _logger;

    private readonly HottaLauncherClient _client;

    private readonly IMemoryCache _memoryCache;


    public HottaGameNoticeProvider(ILogger<HottaGameNoticeProvider> logger, HottaLauncherClient client, IMemoryCache memoryCache)
    {
        _logger = logger;
        _client = client;
        _memoryCache = memoryCache;
    }


    public string ProviderId => HottaGameMapping.ProviderId;


    /// <summary>
    /// 文章都在台服官网上，现在只有台服
    /// </summary>
    public bool Supports(GameKey key) => key == HottaGameMapping.NevernessToEvernessTaiwan;


    public async Task<GameNoticeBoard?> GetBoardAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (!Supports(key))
        {
            return null;
        }
        string cacheKey = $"{nameof(HottaGameNoticeProvider)}_{key}";
        if (!_memoryCache.TryGetValue(cacheKey, out GameNoticeBoard? board))
        {
            string website = HottaLauncherClient.TAIWAN_WEBSITE;
            // 三个分页互不相干，一起抓；某个分页失败只少那一页
            (string PostType, List<HottaNewsItem>? Items)[] lists = await Task.WhenAll(HottaContentMapper.NewsLists.Select(async x =>
                (x.PostType, await TryGetNewsAsync(website, x.Path, cancellationToken))));
            List<(string, List<HottaNewsItem>)> news = lists.Where(x => x.Items is not null).Select(x => (x.PostType, x.Items!)).ToList();
            if (news.Count == 0)
            {
                // 全部失败多半是断网，不缓存，下次再试
                return null;
            }
            board = HottaContentMapper.ToNoticeBoard(news, DateTimeOffset.Now);
            if (board is not null)
            {
                bool partial = news.Count < HottaContentMapper.NewsLists.Count;
                _memoryCache.Set(cacheKey, board, partial ? TimeSpan.FromSeconds(10) : TimeSpan.FromMinutes(1));
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
        string url = item.ContentUrls[0];
        string cacheKey = $"{nameof(HottaGameNoticeProvider)}_{url}";
        if (!_memoryCache.TryGetValue(cacheKey, out string? html))
        {
            html = await _client.GetArticleContentAsync(url, cancellationToken);
            if (html is not null)
            {
                _memoryCache.Set(cacheKey, html, TimeSpan.FromMinutes(10));
            }
        }
        return html;
    }


    private async Task<List<HottaNewsItem>?> TryGetNewsAsync(string website, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.GetNewsAsync(website, path, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Get Neverness to Everness notices: {path}", path);
            return null;
        }
    }

}
