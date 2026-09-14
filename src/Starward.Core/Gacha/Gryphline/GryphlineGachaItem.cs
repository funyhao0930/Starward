using System.Text.Json.Serialization;

namespace Starward.Core.Gacha.Gryphline;

public class GryphlineGachaItem : GachaLogItem
{

    /// <summary>
    /// 这一抽属于哪一期卡池，接口里的 <c>poolId</c> 原文。
    /// <para/>
    /// 特许寻访是一个卡池编号底下的许多期，而保底「在寻访关闭时清零，不会继承」，
    /// 只看卡池编号数墊抽会跨期累加，算出超过保底上限的数字。基础寻访这类常驻池
    /// 的 poolId 始终不变，因此按它分段对所有池子都成立。
    /// </summary>
    [JsonPropertyName("pool_id")]
    public string? PoolId { get; set; }


    public override IGachaType GetGachaType() => new GryphlineGachaType(GachaType);

    public override GryphlineGachaItem Clone() => (GryphlineGachaItem)MemberwiseClone();

}
