using Microsoft.Extensions.Caching.Memory;
using Starward.Core.Games;
using Starward.Core.Games.Kuro;
using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Kuro;
using Starward.Features.GameLauncher;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Kuro;

/// <summary>
/// 鸣潮的启动页横幅与资讯，来自官方启动器的 CDN 配置。
/// <para/>
/// 在这之前只支持启动的游戏那一块整个是空的：横幅与资讯只认 HoYoPlay 的接口。
/// </summary>
internal class KuroLauncherContentProvider : IGameLauncherContentProvider
{

    private readonly KuroLauncherClient _client;

    private readonly IMemoryCache _memoryCache;


    public KuroLauncherContentProvider(KuroLauncherClient client, IMemoryCache memoryCache)
    {
        _client = client;
        _memoryCache = memoryCache;
    }


    public string ProviderId => KuroGameMapping.ProviderId;


    /// <summary>
    /// 接口里的 appId 是国际服的，其他渠道要另配一套，现在只有国际服
    /// </summary>
    public bool Supports(GameKey key) => key == KuroGameMapping.WutheringWavesGlobal;


    public async Task<GameContent?> GetContentAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (!Supports(key))
        {
            return null;
        }
        string language = KuroLauncherClient.GetLanguageCode();
        // 与背景图一样留一分钟的短缓存：切换游戏会反复问同一份配置
        string cacheKey = $"{nameof(KuroLauncherInformation)}_{language}";
        if (!_memoryCache.TryGetValue(cacheKey, out KuroLauncherInformation? information))
        {
            information = await _client.GetInformationAsync(language, cancellationToken);
            if (information is not null)
            {
                _memoryCache.Set(cacheKey, information, TimeSpan.FromMinutes(1));
            }
        }
        return KuroContentMapper.ToGameContent(information);
    }

}
