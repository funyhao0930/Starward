using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Kuro;


/// <summary>
/// 启动器角色接口的外壳。
/// <para/>
/// <see cref="Data"/> 以服务器区域为键，值是<b>再编码过一次</b>的 JSON 字符串，
/// 不是对象，要再解析一次，见 <see cref="KuroPlayerClient"/>。
/// </summary>
public class KuroPlayerResponse
{

    [JsonPropertyName("code")]
    public int Code { get; set; }


    [JsonPropertyName("message")]
    public string? Message { get; set; }


    [JsonPropertyName("data")]
    public Dictionary<string, string>? Data { get; set; }


    /// <summary>
    /// 服务器时间，Unix 毫秒
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

}



/// <summary>
/// 接口返回码，取自官方启动器网页前端
/// </summary>
public static class KuroPlayerResultCode
{

    public const int Success = 0;

    /// <summary>
    /// 登录已过期，要回游戏重新登录一次才会刷新本机的凭证
    /// </summary>
    public const int LoginExpired = 1001;

    public const int AccountCheckFailed = 1002;

    /// <summary>
    /// 这个账号还没有创建角色
    /// </summary>
    public const int NoCharacter = 1003;

    public const int CharacterNotFound = 1004;

    /// <summary>
    /// 服务器缓存里还没有数据，稍等再问一次就有。官方每秒重试一次，最多四次。
    /// </summary>
    public const int RedisEmpty = 1005;

    public const int ServerMaintenance = 1006;

}



/// <summary>
/// 一个服务器区域上的角色概要，来自 <c>/game/queryPlayerInfo</c>
/// </summary>
public class KuroPlayerSummary
{

    [JsonPropertyName("roleId")]
    public string RoleId { get; set; } = "";


    [JsonPropertyName("roleName")]
    public string? RoleName { get; set; }


    /// <summary>
    /// 联觉等级
    /// </summary>
    [JsonPropertyName("level")]
    public int Level { get; set; }


    [JsonPropertyName("sex")]
    public int Sex { get; set; }


    [JsonPropertyName("headPhoto")]
    public long HeadPhoto { get; set; }


    /// <summary>
    /// 服务器区域，如 HMT、Asia、America。接口把它放在字典的键上，解析后回填。
    /// </summary>
    [JsonIgnore]
    public string Region { get; set; } = "";

}



/// <summary>
/// 角色的详细数据，来自 <c>/game/queryRole</c>。
/// <para/>
/// 接口还返回摩托、唱片、装饰等收集数据，启动页用不到，没有建模。
/// </summary>
public class KuroRoleData
{

    [JsonPropertyName("Base")]
    public KuroRoleBase? Base { get; set; }


    [JsonPropertyName("BattlePass")]
    public KuroRoleBattlePass? BattlePass { get; set; }

}



public class KuroRoleBase
{

    [JsonPropertyName("Name")]
    public string? Name { get; set; }


    [JsonPropertyName("Id")]
    public long Id { get; set; }


    /// <summary>
    /// 联觉等级
    /// </summary>
    [JsonPropertyName("Level")]
    public int Level { get; set; }


    [JsonPropertyName("WorldLevel")]
    public int WorldLevel { get; set; }


    [JsonPropertyName("ActiveDays")]
    public int ActiveDays { get; set; }


    [JsonPropertyName("RoleNum")]
    public int RoleNum { get; set; }


    /// <summary>
    /// 结晶波片
    /// </summary>
    [JsonPropertyName("Energy")]
    public int Energy { get; set; }


    [JsonPropertyName("MaxEnergy")]
    public int MaxEnergy { get; set; }


    /// <summary>
    /// 结晶波片回满的时刻，Unix 毫秒。已经满了时是过去的时间或 0。
    /// </summary>
    [JsonPropertyName("EnergyRecoverTime")]
    public long EnergyRecoverTime { get; set; }


    /// <summary>
    /// 结晶单质：结晶波片满了之后溢出存起来的部分
    /// </summary>
    [JsonPropertyName("StoreEnergy")]
    public int StoreEnergy { get; set; }


    [JsonPropertyName("MaxStoreEnergy")]
    public int MaxStoreEnergy { get; set; }


    /// <summary>
    /// 每日活跃度
    /// </summary>
    [JsonPropertyName("Liveness")]
    public int Liveness { get; set; }


    [JsonPropertyName("LivenessMaxCount")]
    public int LivenessMaxCount { get; set; }


    [JsonPropertyName("LivenessUnlock")]
    public bool LivenessUnlock { get; set; }

}



/// <summary>
/// 先约电台
/// </summary>
public class KuroRoleBattlePass
{

    [JsonPropertyName("Level")]
    public int Level { get; set; }


    /// <summary>
    /// 本周已获得的经验，启动器卡片上显示的就是这一对
    /// </summary>
    [JsonPropertyName("WeekExp")]
    public int WeekExp { get; set; }


    [JsonPropertyName("WeekMaxExp")]
    public int WeekMaxExp { get; set; }


    /// <summary>
    /// 当前等级内的经验
    /// </summary>
    [JsonPropertyName("Exp")]
    public int Exp { get; set; }


    [JsonPropertyName("ExpLimit")]
    public int ExpLimit { get; set; }


    [JsonPropertyName("IsUnlock")]
    public bool IsUnlock { get; set; }


    /// <summary>
    /// 两期电台之间会有关闭的空档
    /// </summary>
    [JsonPropertyName("IsOpen")]
    public bool IsOpen { get; set; }

}



/// <summary>
/// 角色接口返回非零代码时抛出
/// </summary>
public class KuroPlayerApiException : Exception
{

    public int Code { get; }

    public KuroPlayerApiException(int code, string? message) : base($"{message} ({code})")
    {
        Code = code;
    }

}
