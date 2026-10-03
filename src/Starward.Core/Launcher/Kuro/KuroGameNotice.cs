using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 鸣潮游戏内的公告清单 <c>gamenotice/G153/{serverId}/notice.json</c>。
/// <para/>
/// 游戏里那块公告是游戏自己画的界面，数据来自这份不需要登录的静态 JSON，
/// 地址取自游戏 SDK 初始化公告网页时带的 <c>cdn</c> 与 <c>serverId</c>。
/// 清单里只有标题与横幅，正文按语言另放一份，见 <see cref="KuroGameNoticeContent"/>。
/// </summary>
public class KuroGameNoticeList
{

    /// <summary>
    /// 游戏公告：版本说明、活动说明、营运声明等
    /// </summary>
    [JsonPropertyName("game")]
    public List<KuroGameNotice>? Game { get; set; }


    /// <summary>
    /// 活动日历、社群资讯这类给玩家看的资讯
    /// </summary>
    [JsonPropertyName("activity")]
    public List<KuroGameNotice>? Activity { get; set; }

}



public class KuroGameNotice
{

    /// <summary>
    /// 现在都是字符串，但同一族的正文里 noticeId 字符串、数字都出现过，这里两种都收
    /// </summary>
    [JsonPropertyName("id")]
    [JsonConverter(typeof(KuroStringOrNumberConverter))]
    public string? Id { get; set; }


    /// <summary>
    /// 正文所在的目录，第一个是主站，其余是备援；目录下按语言放 <c>{lang}.json</c>
    /// </summary>
    [JsonPropertyName("contentPrefix")]
    public List<string>? ContentPrefix { get; set; }


    /// <summary>
    /// 1 表示要提示红点。长期挂着的营运声明是 0。
    /// </summary>
    [JsonPropertyName("red")]
    public int Red { get; set; }


    /// <summary>
    /// 长期公告，不看上下架时间
    /// </summary>
    [JsonPropertyName("permanent")]
    public int Permanent { get; set; }


    [JsonPropertyName("startTimeMs")]
    public long StartTimeMs { get; set; }


    [JsonPropertyName("endTimeMs")]
    public long EndTimeMs { get; set; }


    /// <summary>
    /// 只发给这些渠道，空表示全部
    /// </summary>
    [JsonPropertyName("channel")]
    public List<int>? Channel { get; set; }


    /// <summary>
    /// 只发给名单上的账号，多半是测试用，非空时不显示
    /// </summary>
    [JsonPropertyName("whiteList")]
    public List<string>? WhiteList { get; set; }


    /// <summary>
    /// 列表里的标题，按语言给，键是 <c>zh-Hant</c> 这类脚本写法
    /// </summary>
    [JsonPropertyName("tabTitle")]
    public Dictionary<string, string>? TabTitle { get; set; }


    /// <summary>
    /// 正文上方的横幅，按语言给，每种语言一组
    /// </summary>
    [JsonPropertyName("tabBanner")]
    public Dictionary<string, List<string>>? TabBanner { get; set; }


    /// <summary>
    /// 1 是公告，2 是活动，4 是资讯；游戏内就按这个分页
    /// </summary>
    [JsonPropertyName("category")]
    public int Category { get; set; }

}



/// <summary>
/// 一则公告某种语言的正文
/// </summary>
public class KuroGameNoticeContent
{

    // noticeId 时而是字符串时而是数字，用不到，不读

    [JsonPropertyName("textTitle")]
    public string? TextTitle { get; set; }


    /// <summary>
    /// 正文 HTML，图片都是完整地址
    /// </summary>
    [JsonPropertyName("textContent")]
    public string? TextContent { get; set; }


    [JsonPropertyName("banner")]
    public string? Banner { get; set; }

}



/// <summary>
/// 字符串或数字都读成字符串
/// </summary>
internal class KuroStringOrNumberConverter : JsonConverter<string>
{

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out long value) ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for a notice id."),
        };
    }


    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }

}
