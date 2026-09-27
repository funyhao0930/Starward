using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Gryphline;


/// <summary>
/// 鹰角账号服务（as.gryphline.com）的回应外壳。这家用 status / msg，
/// 与 SKPort 的 code / message 不同。
/// </summary>
public class GryphlineAccountResponse<T>
{

    [JsonPropertyName("status")]
    public int Status { get; set; }


    [JsonPropertyName("msg")]
    public string? Message { get; set; }


    [JsonPropertyName("data")]
    public T? Data { get; set; }

}


public class GryphlineGrantData
{

    /// <summary>
    /// 一次性授权码，拿去换 SKPort 的 cred
    /// </summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

}


/// <summary>
/// SKPort（zonai.skport.com）的回应外壳
/// </summary>
public class SkportResponse<T>
{

    [JsonPropertyName("code")]
    public int Code { get; set; }


    [JsonPropertyName("message")]
    public string? Message { get; set; }


    [JsonPropertyName("data")]
    public T? Data { get; set; }

}


/// <summary>
/// 换到的 SKPort 凭证
/// </summary>
public class SkportCredential
{

    /// <summary>
    /// 放在请求头 <c>cred</c> 里
    /// </summary>
    [JsonPropertyName("cred")]
    public string? Cred { get; set; }


    /// <summary>
    /// 签名用的盐。接口字段名叫 token，但它不是登录令牌，只拿来做 HMAC 的密钥。
    /// </summary>
    [JsonPropertyName("token")]
    public string? Salt { get; set; }


    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

}


public class SkportBindingData
{

    [JsonPropertyName("list")]
    public List<SkportBindingApp>? List { get; set; }

}


/// <summary>
/// 一款游戏的绑定。同一个 SKPort 账号可能同时绑了明日方舟与终末地，
/// 以 <see cref="AppCode"/> 区分。
/// </summary>
public class SkportBindingApp
{

    [JsonPropertyName("appCode")]
    public string? AppCode { get; set; }


    [JsonPropertyName("bindingList")]
    public List<SkportBinding>? BindingList { get; set; }

}


public class SkportBinding
{

    [JsonPropertyName("defaultRole")]
    public SkportRole? DefaultRole { get; set; }


    /// <summary>
    /// 同一个鹰角账号在各服务器上的角色
    /// </summary>
    [JsonPropertyName("roles")]
    public List<SkportRole>? Roles { get; set; }

}


public class SkportRole
{

    [JsonPropertyName("roleId")]
    public string? RoleId { get; set; }


    [JsonPropertyName("serverId")]
    public string? ServerId { get; set; }


    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }


    [JsonPropertyName("level")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Level { get; set; }


    [JsonPropertyName("serverName")]
    public string? ServerName { get; set; }

}


public class SkportCardDetailData
{

    [JsonPropertyName("detail")]
    public SkportCardDetail? Detail { get; set; }

}


/// <summary>
/// 终末地的游戏资料卡。这里只建模卡片要用的几块，
/// 干员、帝江号房间等其余字段不需要。
/// </summary>
public class SkportCardDetail
{

    [JsonPropertyName("base")]
    public SkportCardBase? Base { get; set; }


    [JsonPropertyName("dungeon")]
    public SkportCardDungeon? Dungeon { get; set; }


    [JsonPropertyName("dailyMission")]
    public SkportCardDailyMission? DailyMission { get; set; }


    [JsonPropertyName("bpSystem")]
    public SkportCardBattlePass? BattlePass { get; set; }

}


public class SkportCardBase
{

    [JsonPropertyName("roleId")]
    public string? RoleId { get; set; }


    [JsonPropertyName("name")]
    public string? Name { get; set; }


    [JsonPropertyName("serverName")]
    public string? ServerName { get; set; }


    /// <summary>
    /// 权限等阶
    /// </summary>
    [JsonPropertyName("level")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Level { get; set; }


    /// <summary>
    /// 探索等级
    /// </summary>
    [JsonPropertyName("worldLevel")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int WorldLevel { get; set; }

}


/// <summary>
/// 理智。接口有时把数字写成字符串，一律允许。
/// </summary>
public class SkportCardDungeon
{

    [JsonPropertyName("curStamina")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int CurStamina { get; set; }


    [JsonPropertyName("maxStamina")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int MaxStamina { get; set; }


    /// <summary>
    /// 理智回满的时刻。单位没有公开说明，见 <see cref="SkportCardMapper.ToFullTime"/>。
    /// </summary>
    [JsonPropertyName("maxTs")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long MaxTs { get; set; }

}


public class SkportCardDailyMission
{

    [JsonPropertyName("dailyActivation")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int DailyActivation { get; set; }


    [JsonPropertyName("maxDailyActivation")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int MaxDailyActivation { get; set; }

}


public class SkportCardBattlePass
{

    [JsonPropertyName("curLevel")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int CurLevel { get; set; }


    [JsonPropertyName("maxLevel")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int MaxLevel { get; set; }

}


[JsonSerializable(typeof(GryphlineAccountResponse<GryphlineGrantData>))]
[JsonSerializable(typeof(GryphlineAccountResponse<object>))]
[JsonSerializable(typeof(SkportResponse<SkportCredential>))]
[JsonSerializable(typeof(SkportResponse<SkportBindingData>))]
[JsonSerializable(typeof(SkportResponse<SkportCardDetailData>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
internal partial class SkportJsonContext : JsonSerializerContext
{

}
