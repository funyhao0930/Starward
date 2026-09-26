using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Starward.Core.Games;
using Starward.Core.Games.Kuro;
using Starward.Core.Launcher.Kuro;
using Starward.Features.GameLauncher;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Kuro;

/// <summary>
/// 鸣潮启动页的角色卡片，与官方启动器首页左下角那张是同一份数据。
/// <para/>
/// 凭证来自游戏 SDK 在本机的登录记录（<see cref="KuroSdkAccount"/>），
/// 没在这台电脑上登录过游戏就没有卡片，与官方启动器一致。
/// </summary>
internal class KuroRoleCardProvider : IGameRoleCardProvider
{

    private readonly ILogger<KuroRoleCardProvider> _logger;

    private readonly KuroPlayerClient _client;

    private readonly IMemoryCache _memoryCache;


    public KuroRoleCardProvider(ILogger<KuroRoleCardProvider> logger, KuroPlayerClient client, IMemoryCache memoryCache)
    {
        _logger = logger;
        _client = client;
        _memoryCache = memoryCache;
    }


    public string ProviderId => KuroGameMapping.ProviderId;


    /// <summary>
    /// 接口在国际服的域名上，现在只有国际服
    /// </summary>
    public bool Supports(GameKey key) => key == KuroGameMapping.WutheringWavesGlobal;



    public async Task<IReadOnlyList<GameRoleCardRole>> GetRolesAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (!Supports(key))
        {
            return [];
        }
        List<KuroSdkAccount> accounts = KuroSdkAccount.ReadAll();
        if (accounts.Count == 0)
        {
            return [];
        }
        // 切换游戏会反复进出启动页，账号与角色列表一分钟内不会变。
        // 键里带上凭证：回游戏重新登录后凭证会换，这时应该重新查。
        string cacheKey = $"{nameof(KuroRoleCardProvider)}_{string.Join(',', accounts.Select(x => x.OauthCode))}";
        if (_memoryCache.TryGetValue(cacheKey, out IReadOnlyList<GameRoleCardRole>? cached) && cached is not null)
        {
            return cached;
        }

        var tasks = accounts.Select(async account =>
        {
            try
            {
                List<KuroPlayerSummary> players = await _client.GetPlayersAsync(account.OauthCode, cancellationToken);
                return (Account: account, Players: players, Error: (Exception?)null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (Account: account, Players: new List<KuroPlayerSummary>(), Error: ex);
            }
        }).ToList();
        var results = await Task.WhenAll(tasks);

        var roles = new List<GameRoleCardRole>();
        foreach (var (account, players, _) in results)
        {
            foreach (KuroPlayerSummary player in players)
            {
                roles.Add(new GameRoleCardRole
                {
                    AccountId = account.Cuid,
                    AccountName = account.DisplayName,
                    RoleId = player.RoleId,
                    RoleName = player.RoleName,
                    Level = player.Level,
                    Region = player.Region,
                    RegionName = GetRegionName(player.Region),
                });
            }
        }

        // 只有全部账号都失败才让用户知道；有一个查得到就先显示查得到的。
        // 常见的是其中一个账号很久没登录、凭证过期了。
        if (roles.Count == 0 && results.Select(x => x.Error).FirstOrDefault(x => x is not null) is Exception error)
        {
            _logger.LogWarning(error, "Query Wuthering Waves players failed.");
            throw ToRoleCardException(error);
        }
        foreach (var (account, _, accountError) in results)
        {
            if (accountError is not null)
            {
                _logger.LogInformation("Query Wuthering Waves players of account {cuid} failed: {message}", account.Cuid, accountError.Message);
            }
        }

        _memoryCache.Set(cacheKey, (IReadOnlyList<GameRoleCardRole>)roles, TimeSpan.FromMinutes(1));
        return roles;
    }



    public async Task<GameRoleCardData> GetCardAsync(GameKey key, GameRoleCardRole role, CancellationToken cancellationToken = default)
    {
        // 卡片上只存账号 ID，凭证每次现读：游戏重新登录后旧的会失效
        KuroSdkAccount account = KuroSdkAccount.ReadAll().FirstOrDefault(x => x.Cuid == role.AccountId)
            ?? throw new GameRoleCardException(Lang.GameRoleCard_NoLoginRecord);
        KuroRoleData data;
        try
        {
            data = await _client.GetRoleAsync(account.OauthCode, role.RoleId, role.Region, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Query Wuthering Waves role {region} {roleId} failed.", role.Region, role.RoleId);
            throw ToRoleCardException(ex);
        }
        return ToCardData(role, data);
    }



    internal static GameRoleCardData ToCardData(GameRoleCardRole role, KuroRoleData data)
    {
        KuroRoleBase? b = data.Base;
        KuroRoleBattlePass? bp = data.BattlePass;
        var stats = new List<GameRoleCardStat>();
        if (b is not null)
        {
            DateTimeOffset? fullTime = null;
            if (b.Energy < b.MaxEnergy && b.EnergyRecoverTime > 0)
            {
                fullTime = DateTimeOffset.FromUnixTimeMilliseconds(b.EnergyRecoverTime);
            }
            stats.Add(new GameRoleCardStat
            {
                Name = Lang.GameRoleCard_Kuro_Waveplate,
                IconUri = "ms-appx:///Images/Kuro/WuWa_Waveplate.png",
                Current = b.Energy,
                Max = b.MaxEnergy,
                FullTime = fullTime,
                // 游戏内每 6 分钟恢复 1 点结晶波片
                RecoveryInterval = TimeSpan.FromMinutes(6),
            });
            stats.Add(new GameRoleCardStat
            {
                Name = Lang.GameRoleCard_Kuro_Activity,
                IconUri = "ms-appx:///Images/Kuro/WuWa_Activity.png",
                Current = b.Liveness,
                Max = b.LivenessMaxCount,
                IsLocked = !b.LivenessUnlock,
                Detail = b.LivenessUnlock ? null : Lang.GameRoleCard_ToBeUnlocked,
            });
            stats.Add(new GameRoleCardStat
            {
                Name = Lang.GameRoleCard_Kuro_WaveplateCrystal,
                IconUri = "ms-appx:///Images/Kuro/WuWa_WaveplateCrystal.png",
                Current = b.StoreEnergy,
                Max = b.MaxStoreEnergy,
            });
        }
        if (bp is not null)
        {
            string? detail;
            if (!bp.IsUnlock)
            {
                detail = Lang.GameRoleCard_ToBeUnlocked;
            }
            else if (!bp.IsOpen)
            {
                detail = Lang.GameRoleCard_NotOpen;
            }
            else
            {
                detail = string.Format(Lang.GameRoleCard_Kuro_PodcastLevel, bp.Level);
            }
            stats.Add(new GameRoleCardStat
            {
                Name = Lang.GameRoleCard_Kuro_PioneerPodcast,
                IconUri = "ms-appx:///Images/Kuro/WuWa_PioneerPodcast.png",
                Current = bp.WeekExp,
                Max = bp.WeekMaxExp,
                Detail = detail,
                IsLocked = !bp.IsUnlock || !bp.IsOpen,
            });
        }
        return new GameRoleCardData
        {
            Role = role,
            RoleName = b?.Name ?? role.RoleName,
            Level = b?.Level ?? role.Level,
            LevelLabel = Lang.GameRoleCard_Kuro_UnionLevel,
            UidLabel = Lang.GameRoleCard_Kuro_Uid,
            Stats = stats,
            Footnote = Lang.GameRoleCard_Kuro_Footnote,
        };
    }



    /// <summary>
    /// 把接口的错误换成可以直接显示的文字，文案取自官方启动器
    /// </summary>
    private static GameRoleCardException ToRoleCardException(Exception ex)
    {
        string message = ex switch
        {
            KuroPlayerApiException { Code: KuroPlayerResultCode.LoginExpired or KuroPlayerResultCode.AccountCheckFailed } => Lang.GameRoleCard_LoginExpired,
            KuroPlayerApiException { Code: KuroPlayerResultCode.NoCharacter or KuroPlayerResultCode.CharacterNotFound } => Lang.GameRoleCard_NoCharacter,
            KuroPlayerApiException { Code: KuroPlayerResultCode.ServerMaintenance } => Lang.GameRoleCard_ServerMaintenance,
            HttpRequestException or TaskCanceledException => Lang.Common_NetworkError,
            _ => Lang.GameRoleCard_RefreshFailed,
        };
        return new GameRoleCardException(message, ex);
    }



    /// <summary>
    /// 服务器区域的原始值取自接口；港澳台服的键是 HMT，其余已经是英文名
    /// </summary>
    private static string GetRegionName(string region)
    {
        return region switch
        {
            "HMT" => "TW, HK, MO",
            _ => region,
        };
    }

}
