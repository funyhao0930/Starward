using Starward.Core.Games.HoYo;
using Starward.Core.HoYoPlay;
using Starward.Features.HoYoPlay;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.HoYo;

/// <summary>
/// 把 <see cref="HoYoPlayService"/> 包装成目录 Provider 可用的数据源。
/// 读取时只使用本地缓存，不发起网络请求。
/// </summary>
internal class HoYoGameInfoSource : IHoYoGameInfoSource
{

    private readonly HoYoPlayService _hoYoPlayService;


    public HoYoGameInfoSource(HoYoPlayService hoYoPlayService)
    {
        _hoYoPlayService = hoYoPlayService;
    }


    public IReadOnlyList<GameInfo> GetCachedGameInfos()
    {
        try
        {
            string? json = AppConfig.CachedGameInfo;
            if (!string.IsNullOrWhiteSpace(json))
            {
                return JsonSerializer.Deserialize<List<GameInfo>>(json) ?? [];
            }
        }
        catch { }
        return [];
    }


    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        // 结果会写入 AppConfig.CachedGameInfo
        await _hoYoPlayService.UpdateGameInfoListAsync(cancellationToken);
    }

}
