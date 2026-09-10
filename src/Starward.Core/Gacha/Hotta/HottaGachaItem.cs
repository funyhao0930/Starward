namespace Starward.Core.Gacha.Hotta;

public class HottaGachaItem : GachaLogItem
{

    /// <summary>
    /// 导出文件里的 <c>result_type</c>，原样存下来。
    /// <para/>
    /// 斯卡布罗集市的一行不一定是一抽：棋盘上掷一次骰子（<c>dice</c>）才算一抽，
    /// <c>points_gift</c> 是积分给的奖励、<c>chase_reward</c> 是追猎给的奖励，
    /// 它们与同一次投掷共用时间戳。<b>角色几乎都是从积分来的</b>
    /// （实测限定棋盘 42 个角色里 39 个是 points_gift），
    /// 所以既不能把它们当成抽数，也不能把它们丢掉。
    /// 谁算一抽、谁算稀有度，由应用层的 <c>NteGachaService</c> 判断。
    /// </summary>
    public string? ResultType { get; set; }


    /// <summary>
    /// 导出文件里的 <c>reward_id</c> 原文。
    /// <para/>
    /// <see cref="GachaLogItem.ItemId"/> 是给界面分组用的整数，弧盘那种字符串 ID
    /// 散列过去就回不来了；中文名要按原始 ID 查，所以原样留一份。
    /// </summary>
    public string? RewardId { get; set; }


    public override IGachaType GetGachaType() => new HottaGachaType(GachaType);

    public override HottaGachaItem Clone() => (HottaGachaItem)MemberwiseClone();

}
