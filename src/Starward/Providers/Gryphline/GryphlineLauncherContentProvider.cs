using Microsoft.Extensions.Caching.Memory;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Core.HoYoPlay;
using Starward.Core.Launcher.Gryphline;
using Starward.Features.GameLauncher;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Gryphline;

/// <summary>
/// 终末地的启动页横幅与资讯，来自 GRYPHLINK 的聚合接口。
/// <para/>
/// 横幅与公告是两个 kind，但一次请求就问得完，因此一起缓存。
/// </summary>
internal class GryphlineLauncherContentProvider : IGameLauncherContentProvider
{

    private readonly GryphlineLauncherClient _client;

    private readonly IMemoryCache _memoryCache;


    public GryphlineLauncherContentProvider(GryphlineLauncherClient client, IMemoryCache memoryCache)
    {
        _client = client;
        _memoryCache = memoryCache;
    }


    public string ProviderId => GryphlineGameMapping.ProviderId;


    public bool Supports(GameKey key) => key == GryphlineGameMapping.EndfieldDefault;


    public async Task<GameContent?> GetContentAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (!Supports(key))
        {
            return null;
        }
        string language = GryphlineLauncherClient.GetLanguageCode();
        string cacheKey = $"{nameof(GryphlineContentMapper)}_{language}";
        if (!_memoryCache.TryGetValue(cacheKey, out GameContent? content))
        {
            var (banners, announcements) = await _client.GetLauncherContentAsync(
                GryphlineLauncherClient.ENDFIELD_APP_CODE, language, cancellationToken);
            content = GryphlineContentMapper.ToGameContent(banners, announcements);
            if (content is not null)
            {
                _memoryCache.Set(cacheKey, content, TimeSpan.FromMinutes(1));
            }
        }
        return content;
    }

}
