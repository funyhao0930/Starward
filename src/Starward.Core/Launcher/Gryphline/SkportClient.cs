using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Gryphline;

/// <summary>
/// SKPort（鹰角国际服的社区平台，国服叫森空岛）的游戏资料接口。
/// <para/>
/// 与鸣潮的角色卡不同，这家没有能直接拿来用的本机凭证：GRYPHLINK 启动器本身没有角色卡功能，
/// 游戏 SDK 的登录缓存是真正加密过的。起点只能是玩家登录网页后留下的 <c>ACCOUNT_TOKEN</c>，
/// 之后三步换成 SKPort 的 cred 与签名用的盐：
/// <code>
/// ACCOUNT_TOKEN ─grant→ code ─generate_cred_by_code→ cred + salt
/// </code>
/// 流程与签名算法照社区里实际在跑的签到脚本核对过，见 <see cref="SignV2"/>。
/// </summary>
public class SkportClient
{

    private const string ACCOUNT_API = "https://as.gryphline.com";

    private const string ZONAI_API = "https://zonai.skport.com";


    /// <summary>
    /// SKPort 网页在鹰角账号服务那边的应用标识，换授权码时要带上
    /// </summary>
    private const string SKPORT_APP_CODE = "6eb76d4e13aa36e6";


    /// <summary>
    /// 3 代表 SKPort 的这一套客户端。签名里的 headerJson 与请求头要用同一个值。
    /// </summary>
    private const string PLATFORM = "3";

    private const string VNAME = "1.0.0";


    /// <summary>
    /// 用 SKPort 安卓客户端的 UA：游戏资料那一族端点是给它用的
    /// </summary>
    private const string USER_AGENT = "Skport/0.7.0 (com.gryphline.skport; build:700089; Android 33; ) Okhttp/5.1.0";


    /// <summary>
    /// 终末地在 SKPort 绑定列表里的应用名
    /// </summary>
    public const string ENDFIELD_APP_CODE = "endfield";


    private readonly HttpClient _httpClient;


    public SkportClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
    }



    /// <summary>
    /// 用 <c>ACCOUNT_TOKEN</c> 换 SKPort 的 cred 与签名盐。
    /// <para/>
    /// ACCOUNT_TOKEN 过期或被登出时鹰角账号服务返回非 0 的 status，这里抛
    /// <see cref="SkportLoginExpiredException"/>，调用方据此请玩家重新登录。
    /// </summary>
    public async Task<SkportCredential> ExchangeCredentialAsync(string accountToken, CancellationToken cancellationToken = default)
    {
        using var grantResponse = await _httpClient.PostAsJsonAsync($"{ACCOUNT_API}/user/oauth2/v2/grant",
            new SkportGrantRequest { Token = accountToken, AppCode = SKPORT_APP_CODE, Type = 0 },
            SkportRequestJsonContext.Default.SkportGrantRequest, cancellationToken);
        grantResponse.EnsureSuccessStatusCode();
        var grant = await grantResponse.Content.ReadFromJsonAsync(typeof(GryphlineAccountResponse<GryphlineGrantData>), SkportJsonContext.Default, cancellationToken)
            as GryphlineAccountResponse<GryphlineGrantData>;
        if (grant is null || grant.Status != 0 || string.IsNullOrWhiteSpace(grant.Data?.Code))
        {
            throw new SkportLoginExpiredException(grant?.Message);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ZONAI_API}/web/v1/user/auth/generate_cred_by_code");
        request.Headers.Add("platform", PLATFORM);
        request.Content = JsonContent.Create(new SkportCredRequest { Code = grant.Data.Code, Kind = 1 }, SkportRequestJsonContext.Default.SkportCredRequest);
        using var credResponse = await _httpClient.SendAsync(request, cancellationToken);
        credResponse.EnsureSuccessStatusCode();
        var cred = await credResponse.Content.ReadFromJsonAsync(typeof(SkportResponse<SkportCredential>), SkportJsonContext.Default, cancellationToken)
            as SkportResponse<SkportCredential>;
        if (cred is null || cred.Code != 0 || string.IsNullOrWhiteSpace(cred.Data?.Cred) || string.IsNullOrWhiteSpace(cred.Data?.Salt))
        {
            throw new SkportApiException(cred?.Code ?? -1, cred?.Message);
        }
        return cred.Data;
    }



    /// <summary>
    /// 这个 SKPort 账号绑定的终末地角色，一个都没有时返回空列表
    /// </summary>
    public async Task<List<SkportRole>> GetEndfieldRolesAsync(SkportCredential credential, CancellationToken cancellationToken = default)
    {
        var result = await GetAsync<SkportBindingData>("/api/v1/game/player/binding", credential, null, cancellationToken);
        var roles = new List<SkportRole>();
        foreach (SkportBindingApp app in result?.List ?? [])
        {
            if (!string.Equals(app.AppCode, ENDFIELD_APP_CODE, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            foreach (SkportBinding binding in app.BindingList ?? [])
            {
                // roles 是完整清单；只绑了一个服务器时它可能是空的，只有 defaultRole
                IEnumerable<SkportRole> candidates = binding.Roles is { Count: > 0 } list ? list : binding.DefaultRole is { } d ? [d] : [];
                foreach (SkportRole role in candidates)
                {
                    if (string.IsNullOrWhiteSpace(role.RoleId) || string.IsNullOrWhiteSpace(role.ServerId))
                    {
                        continue;
                    }
                    if (!roles.Any(x => x.RoleId == role.RoleId && x.ServerId == role.ServerId))
                    {
                        roles.Add(role);
                    }
                }
            }
        }
        return roles;
    }



    /// <summary>
    /// 终末地的游戏资料卡，含理智、每日任务与通行证
    /// </summary>
    public async Task<SkportCardDetail> GetEndfieldCardAsync(SkportCredential credential, string roleId, string serverId, CancellationToken cancellationToken = default)
    {
        var result = await GetAsync<SkportCardDetailData>("/api/v1/game/endfield/card/detail", credential, GetGameRoleHeader(roleId, serverId), cancellationToken);
        return result?.Detail ?? throw new SkportApiException(-1, "The card detail is empty.");
    }



    private async Task<T?> GetAsync<T>(string path, SkportCredential credential, string? gameRole, CancellationToken cancellationToken) where T : class
    {
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ZONAI_API}{path}");
        request.Headers.Add("cred", credential.Cred);
        request.Headers.Add("platform", PLATFORM);
        request.Headers.Add("vname", VNAME);
        request.Headers.Add("timestamp", timestamp);
        // 没有 query 也没有 body，签名的 body 部分是空字符串
        request.Headers.Add("sign", SignV2(path, "", timestamp, credential.Salt!));
        request.Headers.Add("sk-language", GetLanguageCode());
        if (gameRole is not null)
        {
            request.Headers.Add("sk-game-role", gameRole);
        }
        request.Headers.TryAddWithoutValidation("User-Agent", USER_AGENT);
        request.Headers.Add("Origin", "https://game.skport.com");
        request.Headers.Add("Referer", "https://game.skport.com/");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync(typeof(SkportResponse<T>), SkportJsonContext.Default, cancellationToken) as SkportResponse<T>;
        if (result is null || result.Code != 0)
        {
            throw new SkportApiException(result?.Code ?? -1, result?.Message);
        }
        return result.Data;
    }



    /// <summary>
    /// SKPort 的 V2 签名：
    /// <code>
    /// headerJson = {"platform":"3","timestamp":"…","dId":"","vName":"1.0.0"}
    /// sign = MD5( hex( HMAC-SHA256( path + body + timestamp + headerJson, key = salt ) ) )
    /// </code>
    /// 几处容易错的地方：headerJson 的键顺序与写法（无空格）要一字不差；
    /// HMAC 的结果先转成小写十六进制字符串，MD5 的是那个字符串，不是原始字节；
    /// GET 请求没有 body 时 body 是空字符串。
    /// </summary>
    public static string SignV2(string path, string body, string timestamp, string salt)
    {
        string headerJson = $$"""{"platform":"{{PLATFORM}}","timestamp":"{{timestamp}}","dId":"","vName":"{{VNAME}}"}""";
        byte[] hmac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(salt), Encoding.UTF8.GetBytes($"{path}{body}{timestamp}{headerJson}"));
        string hmacHex = Convert.ToHexString(hmac).ToLowerInvariant();
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(hmacHex))).ToLowerInvariant();
    }


    /// <summary>
    /// 资料卡要知道查哪个角色，用请求头 <c>sk-game-role</c> 表示：3_角色 ID_服务器 ID
    /// </summary>
    public static string GetGameRoleHeader(string roleId, string serverId) => $"{PLATFORM}_{roleId}_{serverId}";


    /// <summary>
    /// 只影响服务器名称这类文字。社区文档与签到脚本只用过 zh-tw 与 en，
    /// 其余语言不保证接口认得，一律用英文。
    /// </summary>
    private static string GetLanguageCode(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        if (culture.TwoLetterISOLanguageName is "zh"
            && (culture.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase) || culture.Name is "zh-TW" or "zh-HK" or "zh-MO"))
        {
            return "zh-tw";
        }
        return "en";
    }

}



/// <summary>
/// ACCOUNT_TOKEN 已经换不到授权码：过期了，或者玩家在网页上登出了
/// </summary>
public class SkportLoginExpiredException : Exception
{

    public SkportLoginExpiredException(string? message) : base(message ?? "The account token is no longer valid.")
    {

    }

}



/// <summary>
/// SKPort 返回了非 0 的 code
/// </summary>
public class SkportApiException : Exception
{

    public int Code { get; }

    public SkportApiException(int code, string? message) : base($"SKPort returned {code}: {message}")
    {
        Code = code;
    }

}



public class SkportGrantRequest
{

    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("appCode")]
    public string AppCode { get; set; } = "";

    [JsonPropertyName("type")]
    public int Type { get; set; }

}


public class SkportCredRequest
{

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("kind")]
    public int Kind { get; set; }

}


[JsonSerializable(typeof(SkportGrantRequest))]
[JsonSerializable(typeof(SkportCredRequest))]
internal partial class SkportRequestJsonContext : JsonSerializerContext
{

}
