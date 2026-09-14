using System.Text.Json.Serialization;

namespace Starward.Core.Gacha.Gryphline;

public class GryphlineGachaItem : GachaLogItem
{

    /// <summary>
    /// 这一抽属于哪一期卡池，接口里的 <c>poolId</c> 原文。
    /// <para/>
    /// 特许寻访是一个卡池编号底下的许多期。80 抽小保底会继承到后续同类型卡池，
    /// 因此墊抽不按期清零；但福利十连算在哪一期，要靠它才分得出来，
    /// 见 <see cref="IsFree"/>。
    /// </summary>
    [JsonPropertyName("pool_id")]
    public string? PoolId { get; set; }


    /// <summary>
    /// 是不是赠送的一抽，接口里的 <c>isFree</c>。
    /// <para/>
    /// 限定池有两档累计福利，都是赠送一次十连：累计 30 抽送的那次用在本期，
    /// <b>不计入任何保底计数</b>；累计 60 抽送的那次要下一期才能用，
    /// <b>计入下一期的保底</b>。两者都是赠送，只能靠属于哪一期来分。
    /// </summary>
    [JsonPropertyName("is_free")]
    public bool IsFree { get; set; }


    public override IGachaType GetGachaType() => new GryphlineGachaType(GachaType);

    public override GryphlineGachaItem Clone() => (GryphlineGachaItem)MemberwiseClone();

}
