using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using Starward.Core.Games.Hotta;
using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Hotta;
using Starward.Features.GameLauncher;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Hotta;

/// <summary>
/// 异环的启动页横幅与资讯，来自台服官网的 CMS 片段。
/// <para/>
/// 官方外壳首页的那一块本来就是内嵌网页，横幅一个片段、新闻每个分页一个片段，
/// 这里把它们一起抓回来，解析见 <see cref="HottaContentMapper"/>。
/// 与背景图不同，这一路不读本机文件，没装游戏也看得到。
/// </summary>
internal class HottaLauncherContentProvider : IGameLauncherContentProvider
{

    private readonly ILogger<HottaLauncherContentProvider> _logger;

    private readonly HottaLauncherClient _client;

    private readonly IMemoryCache _memoryCache;


    public HottaLauncherContentProvider(ILogger<HottaLauncherContentProvider> logger, HottaLauncherClient client, IMemoryCache memoryCache)
    {
        _logger = logger;
        _client = client;
        _memoryCache = memoryCache;
    }


    public string ProviderId => HottaGameMapping.ProviderId;


    /// <summary>
    /// 片段都在台服官网上，现在只有台服
    /// </summary>
    public bool Supports(GameKey key) => key == HottaGameMapping.NevernessToEvernessTaiwan;


    public async Task<GameContent?> GetContentAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (!Supports(key))
        {
            return null;
        }
        // 与其他供应商一样留一分钟的短缓存：切换游戏会反复问同一批片段
        string cacheKey = $"{nameof(HottaLauncherContentProvider)}_{key}";
        if (!_memoryCache.TryGetValue(cacheKey, out GameContent? content))
        {
            string website = HottaLauncherClient.TAIWAN_WEBSITE;
            // 四个片段互不相干，一起抓。各自失败各自略过：官网改版时某个分页 404，
            // 不该连带横幅与其他分页一起不显示
            Task<List<HottaBanner>?> bannerTask = TryGetAsync(() => _client.GetBannersAsync(website, cancellationToken), "banners", cancellationToken);
            Task<(string PostType, List<HottaNewsItem>? Items)[]> newsTask = Task.WhenAll(HottaContentMapper.NewsLists.Select(async x =>
                (x.PostType, await TryGetAsync(() => _client.GetNewsAsync(website, x.Path, cancellationToken), x.Path, cancellationToken))));
            await Task.WhenAll(bannerTask, newsTask);
            List<HottaBanner>? banners = bannerTask.Result;
            List<(string, List<HottaNewsItem>)> news = newsTask.Result.Where(x => x.Items is not null).Select(x => (x.PostType, x.Items!)).ToList();
            if (banners is null && news.Count == 0)
            {
                // 全部失败多半是断网，不缓存，下次切回来再试
                return null;
            }
            content = HottaContentMapper.ToGameContent(banners, news, DateTimeOffset.Now);
            // 有片段失败时缓存短一些，免得一时的错误要等满一分钟才恢复
            bool partial = banners is null || news.Count < HottaContentMapper.NewsLists.Count;
            if (content is not null)
            {
                _memoryCache.Set(cacheKey, content, partial ? TimeSpan.FromSeconds(10) : TimeSpan.FromMinutes(1));
            }
        }
        return content;
    }


    /// <summary>
    /// 抓一个片段，失败时记下来并返回 null
    /// </summary>
    private async Task<T?> TryGetAsync<T>(Func<Task<T>> get, string name, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await get();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Get Neverness to Everness launcher content: {name}", name);
            return null;
        }
    }

}
