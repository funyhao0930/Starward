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
/// 两者的数据形状、凭证来源都不同，<see cref="GameRoleCard"/> 只认这里的统一模型。
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
    /// 本机可以查询的所有角色，一个都没有时返回空列表，调用方据此把卡片藏起来。
    /// </summary>
    Task<IReadOnlyList<GameRoleCardRole>> GetRolesAsync(GameKey key, CancellationToken cancellationToken = default);


    /// <summary>
    /// 查询单个角色的卡片内容。
    /// <para/>
    /// 失败时抛出 <see cref="GameRoleCardException"/>，其消息可以直接显示给用户。
    /// </summary>
    Task<GameRoleCardData> GetCardAsync(GameKey key, GameRoleCardRole role, CancellationToken cancellationToken = default);

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
