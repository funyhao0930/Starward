using Microsoft.Extensions.Caching.Memory;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Gryphline;
using Starward.Features.Background;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Gryphline;

/// <summary>
/// 终末地的在线背景图，来自 GRYPHLINK 官方启动器的聚合接口。
/// <para/>
/// 这款游戏本机没有现成的背景图可用：官方启动器的默认背景编在
/// Games.exe 的 Qt 资源里（<c>qrc:///web/theme/endfield/default_background.webp</c>），
/// 取不出来。在接上这条接口之前，终末地的启动页只能退到玩家自己的截图。
/// </summary>
internal class GryphlineBackgroundProvider : IGameBackgroundProvider
{

    private readonly GryphlineLauncherClient _client;

    private readonly IMemoryCache _memoryCache;


    public GryphlineBackgroundProvider(GryphlineLauncherClient client, IMemoryCache memoryCache)
    {
        _client = client;
        _memoryCache = memoryCache;
    }


    public string ProviderId => GryphlineGameMapping.ProviderId;


    public bool Supports(GameKey key) => key == GryphlineGameMapping.EndfieldDefault;


    public async Task<List<GameBackground>> GetBackgroundsAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (!Supports(key))
        {
            return [];
        }
        string language = GryphlineLauncherClient.GetLanguageCode();
        // 与其他供应商一样留一分钟的短缓存：切换游戏会反复问同一份配置
        string cacheKey = $"{nameof(GryphlineMainBgImage)}_{language}";
        if (!_memoryCache.TryGetValue(cacheKey, out GryphlineMainBgImage? image))
        {
            image = await _client.GetMainBackgroundImageAsync(GryphlineLauncherClient.ENDFIELD_APP_CODE, language, cancellationToken);
            if (image is not null)
            {
                _memoryCache.Set(cacheKey, image, TimeSpan.FromMinutes(1));
            }
        }
        return GryphlineBackgroundMapper.ToGameBackgrounds(image);
    }

}
