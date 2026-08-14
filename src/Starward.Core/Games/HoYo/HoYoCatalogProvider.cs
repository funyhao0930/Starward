using Starward.Core.HoYoPlay;

namespace Starward.Core.Games.HoYo;

/// <summary>
/// 米哈游游戏目录，以 <see cref="HoYoGameMapping"/> 的静态表为准，
/// 并把 HoYoPlay 接口返回但尚未适配的游戏补充进来。
/// </summary>
public class HoYoCatalogProvider : IGameCatalogProvider
{

    private readonly IHoYoGameInfoSource? _gameInfoSource;


    /// <param name="gameInfoSource">为空时只提供静态表中已适配的游戏</param>
    public HoYoCatalogProvider(IHoYoGameInfoSource? gameInfoSource = null)
    {
        _gameInfoSource = gameInfoSource;
    }


    public string ProviderId => HoYoGameMapping.ProviderId;


    public IReadOnlyList<GameDescriptor> GetGames()
    {
        Dictionary<GameKey, GameInfo> infos = GetGameInfosByKey();
        var games = new List<GameDescriptor>();
        var added = new HashSet<GameKey>();

        foreach (GameKey key in HoYoGameMapping.SupportedGameKeys)
        {
            if (added.Add(key))
            {
                games.Add(CreateDescriptor(key, infos.GetValueOrDefault(key)));
            }
        }

        foreach ((GameKey key, GameInfo info) in infos)
        {
            if (added.Add(key))
            {
                games.Add(CreateDescriptor(key, info));
            }
        }

        return games.AsReadOnly();
    }


    public GameDescriptor? GetGame(GameKey key)
    {
        if (!key.IsProvider(ProviderId) || !key.IsValid || HoYoGameMapping.IsExcluded(key))
        {
            return null;
        }
        GameInfo? info = GetGameInfosByKey().GetValueOrDefault(key);
        if (HoYoGameMapping.IsSupported(key) || info is not null)
        {
            return CreateDescriptor(key, info);
        }
        return null;
    }


    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_gameInfoSource is not null)
        {
            await _gameInfoSource.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }



    private Dictionary<GameKey, GameInfo> GetGameInfosByKey()
    {
        var dic = new Dictionary<GameKey, GameInfo>();
        foreach (GameInfo info in _gameInfoSource?.GetCachedGameInfos() ?? [])
        {
            // 本分支不提供的游戏，不能从接口数据里绕回来
            if (TryGetGameKey(info, out GameKey key) && !HoYoGameMapping.IsExcluded(key))
            {
                dic[key] = info;
            }
        }
        return dic;
    }


    /// <summary>
    /// HoYoPlay 返回的 Bilibili 渠道游戏，其 biz 字段与国服相同，需要按 Id 判断后修正渠道。
    /// </summary>
    private static bool TryGetGameKey(GameInfo info, out GameKey key)
    {
        key = default;
        if (info is null)
        {
            return false;
        }
        if (!HoYoGameMapping.TryFromGameBiz(info.GameBiz, out key))
        {
            return false;
        }
        if (info.IsBilibiliServer())
        {
            key = key with { ChannelId = GameChannelIds.Bilibili };
        }
        return true;
    }


    private static GameDescriptor CreateDescriptor(GameKey key, GameInfo? info)
    {
        bool supported = HoYoGameMapping.IsSupported(key);

        // 已适配的游戏使用内置的名称与图标，未适配的游戏使用 HoYoPlay 接口返回的内容
        string displayName = HoYoGameMapping.GetDisplayName(key);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = info?.Display?.Name ?? key.GameId;
        }
        string iconUri = HoYoGameMapping.GetIconUri(key);
        if (!supported && !string.IsNullOrWhiteSpace(info?.Display?.Icon?.Url))
        {
            iconUri = info.Display.Icon.Url;
        }

        // 已适配的游戏以内置的 HoYoPlay GameId 为准
        string? legacyGameBiz = HoYoGameMapping.TryToGameBiz(key, out GameBiz gameBiz) ? gameBiz.Value : null;
        string? providerGameId = legacyGameBiz is null ? null : GameId.FromGameBiz(gameBiz)?.Id;
        if (string.IsNullOrWhiteSpace(providerGameId))
        {
            providerGameId = info?.Id;
        }

        return new GameDescriptor
        {
            Key = key,
            DisplayName = displayName,
            ChannelName = HoYoGameMapping.GetChannelName(key),
            IconUri = iconUri,
            ChannelIconUri = HoYoGameMapping.GetChannelIconUri(key),
            ThumbnailUri = info?.Display?.Thumbnail?.Url,
            LogoUri = info?.Display?.Logo?.Url,
            ExecutableName = HoYoGameMapping.GetExecutableName(key),
            ProcessName = HoYoGameMapping.GetProcessName(key),
            ScreenshotPaths = HoYoGameMapping.GetScreenshotRelativePath(key) is string screenshot ? [screenshot] : [],
            Capabilities = HoYoGameMapping.GetCapabilities(key),
            LegacyGameBiz = legacyGameBiz,
            ProviderGameId = providerGameId,
        };
    }

}
