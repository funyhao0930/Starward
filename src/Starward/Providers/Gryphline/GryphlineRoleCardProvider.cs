using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Core.Launcher.Gryphline;
using Starward.Features.GameLauncher;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Providers.Gryphline;

/// <summary>
/// 终末地启动页的角色卡片：理智、每日任务、通行证，数据来自 SKPort 的游戏资料卡。
/// <para/>
/// 与鸣潮的卡片最大的不同是要登录：GRYPHLINK 启动器本身没有角色卡功能，
/// 游戏 SDK 的登录缓存又是真正加密过的，本机没有现成的凭证可用。
/// 因此请玩家登录一次鹰角通行证，令牌加密存在本机，见 <see cref="GryphlineAccountStore"/>。
/// </summary>
internal class GryphlineRoleCardProvider : IGameRoleCardProvider
{

    /// <summary>
    /// 与米哈游游戏的实时便笺一样，用体力（理智）当按钮图标
    /// </summary>
    private const string SanityIcon = "ms-appx:///Images/Gryphline/Endfield_Sanity.png";


    /// <summary>
    /// 换到的 cred 能用一阵子，不必每次切页都重新走三步授权
    /// </summary>
    private static readonly TimeSpan CredentialLifetime = TimeSpan.FromMinutes(20);


    /// <summary>
    /// 一次查询最多等多久。鹰角账号服务偶尔要十几秒才回应，
    /// 但不能让卡片无限转圈，超过就当查询失败，玩家可以再按重新整理。
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);


    private readonly ILogger<GryphlineRoleCardProvider> _logger;

    private readonly SkportClient _client;

    private readonly IMemoryCache _memoryCache;


    private SkportCredential? _credential;

    private string? _credentialToken;

    private DateTimeOffset _credentialTime;



    public GryphlineRoleCardProvider(ILogger<GryphlineRoleCardProvider> logger, SkportClient client, IMemoryCache memoryCache)
    {
        _logger = logger;
        _client = client;
        _memoryCache = memoryCache;
    }


    public string ProviderId => GryphlineGameMapping.ProviderId;


    /// <summary>
    /// SKPort 是国际服的平台，国服的森空岛是另一套域名与账号
    /// </summary>
    public bool Supports(GameKey key) => key == GryphlineGameMapping.EndfieldDefault;


    public string? IconUri => SanityIcon;


    public bool RequiresLogin => true;


    public bool IsLoggedIn(GameKey key) => GryphlineAccountStore.HasToken;


    public string? LoginPrompt => Lang.GameRoleCard_Gryphline_LoginPrompt;



    public async Task<bool> LoginAsync(GameKey key, XamlRoot xamlRoot, CancellationToken cancellationToken = default)
    {
        var window = new GryphlineLoginWindow();
        window.Activate();
        string? token = await window.WaitForTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }
        // 先确认令牌真的能用再存下来，免得存了一把换不到授权码的
        try
        {
            SkportCredential credential = await _client.ExchangeCredentialAsync(token, cancellationToken);
            await GryphlineAccountStore.SaveTokenAsync(token);
            SetCredential(token, credential);
            ClearRoleCache();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Verify Gryphline account token after login.");
            throw ToRoleCardException(ex);
        }
    }



    public void Logout(GameKey key)
    {
        GryphlineAccountStore.Clear();
        _credential = null;
        _credentialToken = null;
        ClearRoleCache();
    }



    public async Task<IReadOnlyList<GameRoleCardRole>> GetRolesAsync(GameKey key, CancellationToken cancellationToken = default)
    {
        if (!Supports(key))
        {
            return [];
        }
        string? token = await GryphlineAccountStore.ReadTokenAsync();
        if (token is null)
        {
            // 没有登录，按钮会换成登录入口
            return [];
        }
        // 切换游戏会反复进出启动页，角色列表一分钟内不会变
        if (_memoryCache.TryGetValue(RoleCacheKey, out IReadOnlyList<GameRoleCardRole>? cached) && cached is not null)
        {
            return cached;
        }
        List<SkportRole> roles;
        SkportCredential credential;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            credential = await GetCredentialAsync(token, timeout.Token);
            roles = await _client.GetEndfieldRolesAsync(credential, timeout.Token);
        }
        catch (Exception ex) when (IsFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Query Endfield roles from SKPort failed.");
            throw ToRoleCardException(ex);
        }
        if (roles.Count == 0)
        {
            // 登录了，但这个鹰角通行证没有绑定终末地角色：说清楚，而不是悄悄把按钮藏起来
            throw new GameRoleCardException(Lang.GameRoleCard_Gryphline_NoRole);
        }
        IReadOnlyList<GameRoleCardRole> result = roles.Select(role => new GameRoleCardRole
        {
            AccountId = credential.UserId ?? "",
            RoleId = role.RoleId!,
            RoleName = role.Nickname,
            Level = role.Level,
            Region = role.ServerId!,
            RegionName = role.ServerName,
        }).ToList();
        _memoryCache.Set(RoleCacheKey, result, TimeSpan.FromMinutes(1));
        return result;
    }



    public async Task<GameRoleCardData> GetCardAsync(GameKey key, GameRoleCardRole role, CancellationToken cancellationToken = default)
    {
        string token = await GryphlineAccountStore.ReadTokenAsync()
            ?? throw new GameRoleCardLoginRequiredException(Lang.GameRoleCard_Gryphline_LoginPrompt);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            SkportCardDetail detail;
            try
            {
                detail = await _client.GetEndfieldCardAsync(await GetCredentialAsync(token, timeout.Token), role.RoleId, role.Region, timeout.Token);
            }
            catch (SkportApiException)
            {
                // cred 在缓存期间失效时 SKPort 返回非 0 的 code，重新换一次再试，仍然不行才算失败
                _credential = null;
                detail = await _client.GetEndfieldCardAsync(await GetCredentialAsync(token, timeout.Token), role.RoleId, role.Region, timeout.Token);
            }
            return ToCardData(role, detail, DateTimeOffset.Now);
        }
        catch (Exception ex) when (IsFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Query Endfield card {serverId} {roleId} failed.", role.Region, role.RoleId);
            throw ToRoleCardException(ex);
        }
    }



    internal static GameRoleCardData ToCardData(GameRoleCardRole role, SkportCardDetail detail, DateTimeOffset now)
    {
        var stats = new List<GameRoleCardStat>();
        if (detail.Dungeon is SkportCardDungeon dungeon)
        {
            stats.Add(new GameRoleCardStat
            {
                Name = Lang.GameRoleCard_Gryphline_Sanity,
                IconUri = SanityIcon,
                Current = dungeon.CurStamina,
                Max = dungeon.MaxStamina,
                // 恢复速度没有公开数字，不推算当前值，只倒数到回满
                FullTime = SkportCardMapper.ToFullTime(dungeon, now),
            });
        }
        if (detail.DailyMission is SkportCardDailyMission daily)
        {
            stats.Add(new GameRoleCardStat
            {
                Name = Lang.GameRoleCard_Gryphline_DailyMission,
                // 每日任务与通行证是界面上的概念，不是道具，游戏的道具图里没有它们，用字形图标
                Glyph = "",
                Current = daily.DailyActivation,
                Max = daily.MaxDailyActivation,
            });
        }
        if (detail.BattlePass is SkportCardBattlePass bp)
        {
            stats.Add(new GameRoleCardStat
            {
                Name = Lang.GameRoleCard_Gryphline_BattlePass,
                Glyph = "",
                Current = bp.CurLevel,
                Max = bp.MaxLevel,
            });
        }
        return new GameRoleCardData
        {
            Role = role,
            RoleName = detail.Base?.Name ?? role.RoleName,
            Level = detail.Base?.Level ?? role.Level,
            LevelLabel = Lang.GameRoleCard_Gryphline_AuthorityLevel,
            UidLabel = "UID",
            Stats = stats,
            Footnote = Lang.GameRoleCard_Gryphline_Footnote,
        };
    }



    private async Task<SkportCredential> GetCredentialAsync(string token, CancellationToken cancellationToken)
    {
        if (_credential is not null && _credentialToken == token && DateTimeOffset.Now - _credentialTime < CredentialLifetime)
        {
            return _credential;
        }
        SkportCredential credential = await _client.ExchangeCredentialAsync(token, cancellationToken);
        SetCredential(token, credential);
        return credential;
    }


    private void SetCredential(string token, SkportCredential credential)
    {
        _credential = credential;
        _credentialToken = token;
        _credentialTime = DateTimeOffset.Now;
    }


    /// <summary>
    /// 这次查询该不该当成失败显示给玩家：调用方自己取消的（换游戏、离开页面）不算，
    /// 我们自己设的超时要算——否则卡片只会安静地停在转圈
    /// </summary>
    private static bool IsFailure(Exception ex, CancellationToken callerToken)
    {
        return ex is not OperationCanceledException || !callerToken.IsCancellationRequested;
    }


    private const string RoleCacheKey = $"{nameof(GryphlineRoleCardProvider)}_Roles";

    private void ClearRoleCache() => _memoryCache.Remove(RoleCacheKey);


    /// <summary>
    /// 令牌失效时换成「要重新登录」，卡片据此显示登录入口；其余错误照常显示原因
    /// </summary>
    private static GameRoleCardException ToRoleCardException(Exception ex)
    {
        return ex switch
        {
            GameRoleCardException e => e,
            SkportLoginExpiredException => new GameRoleCardLoginRequiredException(Lang.GameRoleCard_Gryphline_LoginExpired, ex),
            _ => new GameRoleCardException(Lang.GameRoleCard_RefreshFailed, ex),
        };
    }

}
