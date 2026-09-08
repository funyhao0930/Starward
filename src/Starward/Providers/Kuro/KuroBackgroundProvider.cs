using Microsoft.Extensions.Caching.Memory;
using Starward.Core.Games;
using Starward.Core.Games.Kuro;
using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Kuro;
using Starward.Features.Background;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Kuro;

/// <summary>
/// 鸣潮的在线背景图，来自官方启动器的 CDN 配置。
/// <para/>
/// 在这之前鸣潮走的是本机路线：把官方启动器缓存的逐帧序列
/// （<c>kr_game_cache\animate_bg</c>）合成视频当背景。那条路的问题是影格只在官方启动器
/// 自己跑过一次之后才有，而且不随游戏版本更新——本机那份可以停在一年多前的版本上。
/// 官方接口直接给现成的 mp4，既跟得上版本，也不必留着上百 MB 的影格。
/// </summary>
internal class KuroBackgroundProvider : IGameBackgroundProvider
{

    private readonly KuroLauncherClient _client;

    private readonly IMemoryCache _memoryCache;


    public KuroBackgroundProvider(KuroLauncherClient client, IMemoryCache memoryCache)
    {
        _client = client;
        _memoryCache = memoryCache;
    }


    public string ProviderId => KuroGameMapping.ProviderId;


    /// <summary>
    /// 接口里的 appId 是国际服的，其他渠道要另配一套，现在只有国际服
    /// </summary>
    public bool Supports(GameKey key) => key == KuroGameMapping.WutheringWavesGlobal;


    public async Task<List<GameBackground>> GetBackgroundsAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (!Supports(key))
        {
            return [];
        }
        string language = KuroLauncherClient.GetLanguageCode();
        // 与 HoYoPlayService 一样留一分钟的短缓存：切换游戏会反复问同一份配置
        string cacheKey = $"{nameof(KuroLauncherBackground)}_{language}";
        if (!_memoryCache.TryGetValue(cacheKey, out KuroLauncherBackground? background))
        {
            background = await _client.GetBackgroundAsync(language, cancellationToken);
            if (background is not null)
            {
                _memoryCache.Set(cacheKey, background, TimeSpan.FromMinutes(1));
            }
        }
        return KuroBackgroundMapper.ToGameBackgrounds(background);
    }

}
