using System.Text.Json.Serialization;

namespace Starward.Core.Gacha.Hotta;

/// <summary>
/// nte-exporter（Golumpa/nte-exporter）导出的抽卡记录文件。
/// <para/>
/// 异环的抽卡记录只走游戏自己的 RPC（<c>LotteryRecord_Req/Rsp</c>），没有任何 HTTP 接口，
/// 所以 Starward 读的是玩家用抓包工具导出的这份 JSON，而不是自己去取。
/// <para/>
/// 该工具一个卡池写一个文件，玩家手上通常有好几份。文件长这样：
/// <code>
/// {
///   "format": "nte-history-export",
///   "format_version": 1,
///   "banner": { "id": "Lottery_LimitedCharacter", ... },
///   "scan": { "warnings": [ ... ] },
///   "user_uid": "218216016349",
///   "records": [ { "pool_group_id": "...", "timestamp": "2026-06-10 13:32:17", ... } ]
/// }
/// </code>
/// 字段全部按「存在与否」解析：该工具至今没有为改结构升过 <c>format_version</c>，
/// 可选字段是整个省掉而不是写成 null，因此每个字段都得当成可能不在。
/// </summary>
public class HottaExportFile
{

    /// <summary>
    /// 抽卡记录文件固定是 <see cref="HistoryFormat"/>，成就文件是另一种格式
    /// </summary>
    public const string HistoryFormat = "nte-history-export";

    /// <summary>
    /// 同一个目录下还会有成就导出，格式完全不同，得认出来单独提示
    /// </summary>
    public const string AchievementFormat = "nte-achievement-export";


    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("format_version")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int FormatVersion { get; set; }

    [JsonPropertyName("game")]
    public string? Game { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("exporter")]
    public HottaExportTool? Exporter { get; set; }

    [JsonPropertyName("banner")]
    public HottaExportBanner? Banner { get; set; }

    [JsonPropertyName("scan")]
    public HottaExportScan? Scan { get; set; }

    /// <summary>
    /// 玩家 uid，12 位十进制字符串。工具没认出 uid 时整个字段不写，文件名也变成 <c>unknown_*</c>。
    /// </summary>
    [JsonPropertyName("user_uid")]
    public string? UserUid { get; set; }

    [JsonPropertyName("server_id")]
    public string? ServerId { get; set; }

    [JsonPropertyName("records")]
    public List<HottaExportRecord>? Records { get; set; }

}


public class HottaExportTool
{

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

}


/// <summary>
/// 顶层的卡池信息，是拿第一条记录推断出来的，记录为空时还会谎报成常驻池，
/// 因此只当显示用，判断卡池一律看记录自己的 <see cref="HottaExportRecord.PoolGroupId"/>。
/// </summary>
public class HottaExportBanner
{

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

}


public class HottaExportScan
{

    [JsonPropertyName("warnings")]
    public List<HottaExportWarning>? Warnings { get; set; }

}


/// <summary>
/// 抓包过程中的问题。<c>PAGE_GAP_DETECTED</c> 表示中间断页、后面的记录被丢掉了，
/// <c>DID_NOT_START_AT_PAGE_1</c> 表示这份导出本身就可能不完整，都得让用户知道。
/// </summary>
public class HottaExportWarning
{

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

}


/// <summary>
/// 一条记录。三个卡池的字段不完全一样（棋盘有 <see cref="ResultType"/> 与骰子点数，
/// 弧盘奇迹盒没有），缺的字段解析成 null 即可。
/// </summary>
public class HottaExportRecord
{

    /// <summary>
    /// 工具自己合成的记录 ID，32 位十六进制。
    /// <para/>
    /// 不能拿它当 Starward 的记录 ID：它散列的是原始时间戳字节，
    /// 而该工具两条解码路径的时间戳刻度差 4 倍，同一抽在两次导出里可能得到不同的值。
    /// </summary>
    [JsonPropertyName("uid")]
    public string? Uid { get; set; }

    [JsonPropertyName("pool_group_id")]
    public string? PoolGroupId { get; set; }

    /// <summary>
    /// <c>yyyy-MM-dd HH:mm:ss</c>，该工具按 UTC 渲染，字符串里没有时区标记
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>
    /// 同一时间戳内的序号，0 是最新的那条。
    /// <para/>
    /// 可空是为了容错：这个字段该工具一直都写，但真遇上写成 null 的文件，
    /// 不该整份拒收，当没有序号处理即可。
    /// </summary>
    [JsonPropertyName("timestamp_group_ordinal")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? TimestampGroupOrdinal { get; set; }

    /// <summary>
    /// 棋盘卡池才有：<c>dice</c> 才是掷出去的一抽，
    /// <c>points_gift</c>、<c>chase_reward</c> 是同一抽附带的奖励，不计入抽数与保底。
    /// 弧盘奇迹盒没有这个字段，神秘盒子固定是 <c>single_pull</c>。
    /// </summary>
    [JsonPropertyName("result_type")]
    public string? ResultType { get; set; }

    /// <summary>
    /// <c>character</c> / <c>arc</c> / <c>item</c> / <c>cosmetic</c>，解不出来时是空串
    /// </summary>
    [JsonPropertyName("reward_type")]
    public string? RewardType { get; set; }

    [JsonPropertyName("reward_id")]
    public string? RewardId { get; set; }

    /// <summary>
    /// 名字对不上时，棋盘与神秘盒子写空串，弧盘奇迹盒写字面量 <c>UNKNOWN</c>
    /// </summary>
    [JsonPropertyName("reward_name")]
    public string? RewardName { get; set; }

    /// <summary>
    /// 稀有度是字母 <c>S</c> / <c>A</c> / <c>B</c>，不是星数；
    /// 对不上时棋盘与神秘盒子给 null，弧盘奇迹盒给空串
    /// </summary>
    [JsonPropertyName("reward_rank")]
    public string? RewardRank { get; set; }

    [JsonPropertyName("quantity")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? Quantity { get; set; }

}


[JsonSerializable(typeof(HottaExportFile))]
internal partial class HottaGachaJsonContext : JsonSerializerContext
{

}
