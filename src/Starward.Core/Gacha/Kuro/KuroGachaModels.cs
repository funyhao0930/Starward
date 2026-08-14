using System.Text.Json.Serialization;

namespace Starward.Core.Gacha.Kuro;

/// <summary>
/// 唤取记录接口的请求体
/// </summary>
public class KuroGachaQueryBody
{

    [JsonPropertyName("playerId")]
    public string PlayerId { get; set; }

    [JsonPropertyName("serverId")]
    public string ServerId { get; set; }

    [JsonPropertyName("cardPoolId")]
    public string CardPoolId { get; set; }

    [JsonPropertyName("cardPoolType")]
    public string CardPoolType { get; set; }

    [JsonPropertyName("languageCode")]
    public string LanguageCode { get; set; }

    [JsonPropertyName("recordId")]
    public string RecordId { get; set; }

    [JsonPropertyName("pageNum")]
    public int PageNum { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

}


public class KuroGachaResponse
{

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public List<KuroGachaRecord>? Data { get; set; }

}


public class KuroGachaRecord
{

    [JsonPropertyName("resourceId")]
    public long ResourceId { get; set; }

    [JsonPropertyName("qualityLevel")]
    public int QualityLevel { get; set; }

    [JsonPropertyName("resourceType")]
    public string? ResourceType { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>
    /// 接口返回的是 <c>yyyy-MM-dd HH:mm:ss</c> 字符串，没有时区，按本地时间处理
    /// </summary>
    [JsonPropertyName("time")]
    public string? Time { get; set; }

}


[JsonSerializable(typeof(KuroGachaQueryBody))]
[JsonSerializable(typeof(KuroGachaResponse))]
internal partial class KuroGachaJsonContext : JsonSerializerContext
{

}
