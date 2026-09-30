using Microsoft.Extensions.Caching.Memory;
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

    private readonly HottaLauncherClient _client;

    private readonly IMemoryCache _memoryCache;


    public HottaLauncherContentProvider(HottaLauncherClient client, IMemoryCache memoryCache)
    {
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
            // 四个片段互不相干，一起抓
            Task<List<HottaBanner>> bannerTask = _client.GetBannersAsync(website, cancellationToken);
            Task<(string, List<HottaNewsItem>)[]> newsTask = Task.WhenAll(HottaContentMapper.NewsLists.Select(async x =>
                (x.PostType, await _client.GetNewsAsync(website, x.Path, cancellationToken))));
            await Task.WhenAll(bannerTask, newsTask);
            content = HottaContentMapper.ToGameContent(bannerTask.Result, newsTask.Result, DateTimeOffset.Now);
            if (content is not null)
            {
                _memoryCache.Set(cacheKey, content, TimeSpan.FromMinutes(1));
            }
        }
        return content;
    }

}
