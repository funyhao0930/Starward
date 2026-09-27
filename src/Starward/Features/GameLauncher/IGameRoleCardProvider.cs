using Microsoft.UI.Xaml;
using Starward.Core.Games;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 提供启动页的角色卡片：角色名、等级、UID 与几项体力类数值。
/// <para/>
/// 米哈游的同类功能是实时便笺（<c>DailyNoteButton</c>），要米游社 / HoYoLAB 账号；
/// 鸣潮官方启动器首页也有一张，凭证是游戏 SDK 留在本机的登录记录，不用另外登录。
/// 两者的数据形状、凭证来源都不同，<see cref="GameRoleCardButton"/> 只认这里的统一模型。
/// <para/>
/// 定义在应用层而不是 Starward.Core：标签文字与图标要用应用层的多语言资源与图片。
/// </summary>
public interface IGameRoleCardProvider
{

    /// <summary>
    /// 供应商标识，与 <see cref="GameKey.ProviderId"/> 对应
    /// </summary>
    string ProviderId { get; }


    /// <summary>
    /// 该游戏有没有角色卡片接口
    /// </summary>
    bool Supports(GameKey key);


    /// <summary>
    /// 启动页侧栏按钮的图标，一般用该游戏的体力图标
    /// </summary>
    string? IconUri { get; }


    /// <summary>
    /// 本机可以查询的所有角色，一个都没有时返回空列表，调用方据此把卡片藏起来。
    /// </summary>
    Task<IReadOnlyList<GameRoleCardRole>> GetRolesAsync(GameKey key, CancellationToken cancellationToken = default);


    /// <summary>
    /// 查询单个角色的卡片内容。
    /// <para/>
    /// 失败时抛出 <see cref="GameRoleCardException"/>，其消息可以直接显示给用户。
    /// </summary>
    Task<GameRoleCardData> GetCardAsync(GameKey key, GameRoleCardRole role, CancellationToken cancellationToken = default);


    /// <summary>
    /// 要先在 Starward 里登录才查得到。
    /// <para/>
    /// 鸣潮的凭证是游戏自己留在本机的，不用登录；终末地没有这样的凭证，只能请玩家登录鹰角通行证。
    /// 需要登录的游戏在还没登录时也要显示按钮，卡片里放登录入口，否则玩家根本找不到这个功能。
    /// </summary>
    bool RequiresLogin => false;


    /// <summary>
    /// 是否已经登录。只看本机有没有存下凭证，不代表凭证一定还有效。
    /// </summary>
    bool IsLoggedIn(GameKey key) => true;


    /// <summary>
    /// 还没登录时卡片上的说明，告诉玩家登录之后能看到什么
    /// </summary>
    string? LoginPrompt => null;


    /// <summary>
    /// 请玩家登录，成功返回 true，取消或失败返回 false
    /// </summary>
    Task<bool> LoginAsync(GameKey key, XamlRoot xamlRoot, CancellationToken cancellationToken = default) => Task.FromResult(false);


    /// <summary>
    /// 删掉本机存下的凭证
    /// </summary>
    void Logout(GameKey key) { }

}



/// <summary>
/// 可以在卡片上切换的一个角色
/// </summary>
public class GameRoleCardRole
{

    /// <summary>
    /// 所属账号，同一个账号可能在多个服务器上各有角色
    /// </summary>
    public string AccountId { get; set; } = "";

    public string? AccountName { get; set; }

    public string RoleId { get; set; } = "";

    public string? RoleName { get; set; }

    public int Level { get; set; }

    /// <summary>
    /// 服务器区域的原始值，查询时要带回去
    /// </summary>
    public string Region { get; set; } = "";

    /// <summary>
    /// 给人看的服务器名称
    /// </summary>
    public string? RegionName { get; set; }


    /// <summary>
    /// 记住上次选择用的键
    /// </summary>
    public string SelectionKey => $"{AccountId}|{Region}|{RoleId}";

}



/// <summary>
/// 卡片内容
/// </summary>
public class GameRoleCardData
{

    public GameRoleCardRole Role { get; set; } = null!;

    public string? RoleName { get; set; }

    public int Level { get; set; }

    /// <summary>
    /// 等级的叫法，如「联觉等级」
    /// </summary>
    public string? LevelLabel { get; set; }

    /// <summary>
    /// UID 的叫法，如「特征码」
    /// </summary>
    public string? UidLabel { get; set; }

    public IReadOnlyList<GameRoleCardStat> Stats { get; set; } = [];

    /// <summary>
    /// 卡片底部的小字说明
    /// </summary>
    public string? Footnote { get; set; }

}



/// <summary>
/// 卡片上的一项数值，如「结晶波片 5/240」
/// </summary>
public class GameRoleCardStat
{

    public string Name { get; set; } = "";

    public string? IconUri { get; set; }

    /// <summary>
    /// 没有图片时改画的字形图标（Segoe Fluent Icons）。
    /// 有些数值是界面上的概念而不是道具，游戏的道具图里根本没有它们。
    /// </summary>
    public string? Glyph { get; set; }

    public int Current { get; set; }

    public int Max { get; set; }

    /// <summary>
    /// 数值下方的一行小字，如电台等级。与 <see cref="FullTime"/> 同时给出时以倒计时为准。
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// 回满的时刻。给出时卡片自己按分钟倒数，不必重新查询。
    /// </summary>
    public DateTimeOffset? FullTime { get; set; }

    /// <summary>
    /// 每恢复一点要多久。与 <see cref="FullTime"/> 一起给出时，卡片会按剩余时间推算当前值，
    /// 不必等下一次查询。
    /// </summary>
    public TimeSpan? RecoveryInterval { get; set; }

    /// <summary>
    /// 功能尚未解锁，只显示 <see cref="Detail"/>
    /// </summary>
    public bool IsLocked { get; set; }

}



/// <summary>
/// 查询角色卡片失败，消息已经本地化，可以直接显示
/// </summary>
public class GameRoleCardException : Exception
{

    public GameRoleCardException(string message, Exception? innerException = null) : base(message, innerException)
    {

    }

}



/// <summary>
/// 凭证已经失效，要请玩家重新登录。卡片据此换成登录入口，而不是只显示一句错误。
/// </summary>
public class GameRoleCardLoginRequiredException : GameRoleCardException
{

    public GameRoleCardLoginRequiredException(string message, Exception? innerException = null) : base(message, innerException)
    {

    }

}
