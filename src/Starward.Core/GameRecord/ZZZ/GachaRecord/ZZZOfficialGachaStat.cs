using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.ZZZ.GachaRecord;

// HoYoLAB「調頻統計工具」(act.hoyolab.com/zzz/event/gacha-record) 用的三支接口：
// index 给各频段的总抽数与当前垫抽，cur_pool_stat / hist_pool_stat 给每个卡池的 S 级出货。
// 只有 S 级，没有 A/B 级逐抽记录；Sort 是该频段自开服起的累计抽数，Count 是这只 S 用了几抽。


public class ZZZOfficialGachaIndex
{

    [JsonPropertyName("type_stats")]
    public List<ZZZOfficialGachaTypeStat> TypeStats { get; set; } = new();


    [JsonPropertyName("cur_ver")]
    public string CurrentVersion { get; set; } = "";

}


public class ZZZOfficialGachaTypeStat
{

    /// <summary>
    /// GACHA_TYPE_CHARACTER_UP 等字串
    /// </summary>
    [JsonPropertyName("gacha_type")]
    public string GachaType { get; set; } = "";


    [JsonPropertyName("total_count")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int TotalCount { get; set; }


    [JsonPropertyName("s_count")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int SCount { get; set; }


    [JsonPropertyName("up_count")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int UpCount { get; set; }


    /// <summary>
    /// 距离上一只 S 已经垫了几抽
    /// </summary>
    [JsonPropertyName("next_s_cur_times")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int CurrentPity { get; set; }


    [JsonPropertyName("has_data_vers")]
    public List<string> HasDataVersions { get; set; } = new();


    /// <summary>
    /// 对应 ZZZGachaType 的数值，未知类型为 0
    /// </summary>
    [JsonIgnore]
    public int GachaTypeValue => ToGachaTypeValue(GachaType);


    public static int ToGachaTypeValue(string? gachaType) => gachaType switch
    {
        "GACHA_TYPE_PERMANENT" => 1,
        "GACHA_TYPE_CHARACTER_UP" => 2,
        "GACHA_TYPE_WEAPON_UP" => 3,
        "GACHA_TYPE_BANGBOO" => 5,
        "GACHA_TYPE_CHARACTER_RETURN" => 102,
        "GACHA_TYPE_WEAPON_RETURN" => 103,
        _ => 0,
    };

}


public class ZZZOfficialGachaPoolList
{

    [JsonPropertyName("list")]
    public List<ZZZOfficialGachaPool> List { get; set; } = new();

}


public class ZZZOfficialGachaPool
{

    [JsonPropertyName("gacha_type")]
    public string GachaType { get; set; } = "";


    [JsonPropertyName("version")]
    public string Version { get; set; } = "";


    [JsonPropertyName("cur_records")]
    public List<ZZZOfficialGachaRecord> Records { get; set; } = new();

}


public class ZZZOfficialGachaRecord
{

    [JsonPropertyName("item_id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int ItemId { get; set; }


    [JsonPropertyName("name")]
    public string Name { get; set; } = "";


    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "";


    /// <summary>
    /// ItemType_Avatar / ItemType_Weapon / ItemType_Bangboo
    /// </summary>
    [JsonPropertyName("item_type")]
    public string ItemType { get; set; } = "";


    [JsonPropertyName("rarity")]
    public string Rarity { get; set; } = "";


    [JsonPropertyName("is_up")]
    public bool IsUp { get; set; }


    /// <summary>
    /// 这只 S 用了几抽
    /// </summary>
    [JsonPropertyName("count")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Count { get; set; }


    /// <summary>
    /// Unix 秒
    /// </summary>
    [JsonPropertyName("record_time")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long RecordTime { get; set; }


    /// <summary>
    /// 该频段自开服起的第几抽
    /// </summary>
    [JsonPropertyName("sort")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Sort { get; set; }

}
