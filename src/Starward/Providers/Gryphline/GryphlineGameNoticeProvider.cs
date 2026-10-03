using Microsoft.Extensions.Caching.Memory;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Core.Launcher;
using Starward.Core.Launcher.Gryphline;
using Starward.Features.GameLauncher;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Gryphline;

/// <summary>
/// 终末地的游戏内公告，接口连正文一起返回，不需要另外取正文。
/// </summary>
internal class GryphlineGameNoticeProvider : IGameNoticeProvider
{

    private readonly GryphlineLauncherClient _client;

    private readonly IMemoryCache _memoryCache;


    public GryphlineGameNoticeProvider(GryphlineLauncherClient client, IMemoryCache memoryCache)
    {
        _client = client;
        _memoryCache = memoryCache;
    }


    public string ProviderId => GryphlineGameMapping.ProviderId;


    public bool Supports(GameKey key) => key == GryphlineGameMapping.EndfieldDefault;


    public async Task<GameNoticeBoard?> GetBoardAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (!Supports(key))
        {
            return null;
        }
        string language = GryphlineLauncherClient.GetLanguageCode();
        string server = GryphlineLauncherClient.GetBulletinServer(DateTimeOffset.Now.Offset);
        string cacheKey = $"{nameof(GryphlineGameNoticeProvider)}_{language}_{server}";
        if (!_memoryCache.TryGetValue(cacheKey, out GameNoticeBoard? board))
        {
            GryphlineBulletinData? data = await _client.GetBulletinAsync(language, server, cancellationToken);
            board = GryphlineNoticeMapper.ToNoticeBoard(data);
            if (board is not null)
            {
                _memoryCache.Set(cacheKey, board, TimeSpan.FromMinutes(1));
            }
        }
        return board;
    }


    /// <summary>
    /// 正文已经随列表拿到，走不到这里
    /// </summary>
    public Task<string?> GetContentHtmlAsync(GameKey key, GameNoticeItem item, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(item.ContentHtml);
    }

}
