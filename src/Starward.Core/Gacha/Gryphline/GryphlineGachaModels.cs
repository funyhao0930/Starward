using System.Text.Json.Serialization;

namespace Starward.Core.Gacha.Gryphline;

public class GryphlineGachaResponse
{

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public GryphlineGachaPage? Data { get; set; }

}


public class GryphlineGachaPage
{

    [JsonPropertyName("list")]
    public List<GryphlineGachaRecord>? List { get; set; }

    /// <summary>
    /// 还有更早的记录时为 true，翻页游标是本页最后一条的 <see cref="GryphlineGachaRecord.SeqId"/>
    /// </summary>
    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }

}


public class GryphlineGachaRecord
{

    /// <summary>
    /// 服务器端的记录 ID，单调递增，Starward 直接拿它去重
    /// </summary>
    [JsonPropertyName("seqId")]
    public string? SeqId { get; set; }

    [JsonPropertyName("charId")]
    public string? CharId { get; set; }

    [JsonPropertyName("charName")]
    public string? CharName { get; set; }

    [JsonPropertyName("weaponId")]
    public string? WeaponId { get; set; }

    [JsonPropertyName("weaponName")]
    public string? WeaponName { get; set; }

    [JsonPropertyName("weaponType")]
    public string? WeaponType { get; set; }

    [JsonPropertyName("rarity")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Rarity { get; set; }

    /// <summary>
    /// 抽取时间，Unix 毫秒
    /// </summary>
    [JsonPropertyName("gachaTs")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long GachaTs { get; set; }

    [JsonPropertyName("poolId")]
    public string? PoolId { get; set; }

    [JsonPropertyName("poolName")]
    public string? PoolName { get; set; }

    [JsonPropertyName("isNew")]
    public bool IsNew { get; set; }

    [JsonPropertyName("isFree")]
    public bool IsFree { get; set; }

}


[JsonSerializable(typeof(GryphlineGachaResponse))]
internal partial class GryphlineGachaJsonContext : JsonSerializerContext
{

}
