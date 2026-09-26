using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 游戏 SDK 在本机留下的一个登录记录。
/// <para/>
/// 官方启动器的角色卡片就是拿它去查角色的：启动器本身没有登录，
/// 而是透过 <c>kr_get_sdk_user_launcher_cache</c> 读取游戏 SDK 写在
/// <c>%APPDATA%\KR_G153\{SDK 产品号}\KRSDKUserLauncherCache.json</c> 里的记录，
/// 里面的 <see cref="OauthCode"/> 就是查询接口要的凭证。
/// 因此没在本机登录过游戏的账号，官方启动器与 Starward 都查不到。
/// </summary>
public class KuroSdkAccount
{

    /// <summary>
    /// 账号 ID，官方埋点叫 cuid
    /// </summary>
    [JsonPropertyName("cuid")]
    public string Cuid { get; set; } = "";


    /// <summary>
    /// 形如 U123456789A 的账号名
    /// </summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }


    /// <summary>
    /// 第三方登录（Google、Apple 等）带回来的昵称
    /// </summary>
    [JsonPropertyName("thirdNickName")]
    public string? ThirdNickName { get; set; }


    [JsonPropertyName("email")]
    public string? Email { get; set; }


    [JsonPropertyName("loginType")]
    public int LoginType { get; set; }


    /// <summary>
    /// 查询凭证，文件里是混淆过的，读取后已经还原
    /// </summary>
    [JsonPropertyName("oauthCode")]
    public string OauthCode { get; set; } = "";


    /// <summary>
    /// 给人看的账号名称：昵称优先，其次邮箱，最后账号名
    /// </summary>
    [JsonIgnore]
    public string DisplayName => FirstNonEmpty(ThirdNickName, Email, Username, Cuid);



    /// <summary>
    /// SDK 的数据目录，其下每个子目录是一个 SDK 产品号（国际服是 A1730）
    /// </summary>
    public static string DefaultRootFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KR_G153");


    private const string CacheFileName = "KRSDKUserLauncherCache.json";


    /// <summary>
    /// 读出本机所有的登录记录，一个都没有时返回空列表。
    /// <para/>
    /// 不写死 SDK 产品号：换渠道或 SDK 升级时子目录名会变，逐个子目录找缓存文件即可。
    /// 同一个账号出现在多个子目录时只保留一份。
    /// </summary>
    public static List<KuroSdkAccount> ReadAll(string? rootFolder = null)
    {
        rootFolder ??= DefaultRootFolder;
        var accounts = new List<KuroSdkAccount>();
        if (!Directory.Exists(rootFolder))
        {
            return accounts;
        }
        foreach (string folder in Directory.EnumerateDirectories(rootFolder))
        {
            string file = Path.Combine(folder, CacheFileName);
            if (!File.Exists(file))
            {
                continue;
            }
            try
            {
                foreach (KuroSdkAccount account in Parse(File.ReadAllText(file)))
                {
                    if (!accounts.Exists(x => x.Cuid == account.Cuid))
                    {
                        accounts.Add(account);
                    }
                }
            }
            // 文件被游戏占用或格式损坏时跳过这一份，不影响其他目录
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
        }
        return accounts;
    }



    /// <summary>
    /// 解析缓存文件的内容并还原凭证，没有凭证的记录会被丢弃
    /// </summary>
    public static List<KuroSdkAccount> Parse(string json)
    {
        var list = JsonSerializer.Deserialize(json, KuroPlayerJsonContext.Default.ListKuroSdkAccount) ?? [];
        list.RemoveAll(x => string.IsNullOrWhiteSpace(x.Cuid) || string.IsNullOrWhiteSpace(x.OauthCode));
        foreach (KuroSdkAccount account in list)
        {
            account.OauthCode = Deobfuscate(account.OauthCode);
        }
        return list;
    }



    /// <summary>
    /// 文件里的凭证每个字符都与 5 做了异或。
    /// <para/>
    /// 取自官方启动器的日志：<c>kr_get_sdk_user_launcher_cache</c> 回给网页的值是
    /// <c>fcd`2746-…</c>，网页随后发给 <c>/game/queryPlayerInfo</c> 的是 <c>cfae7213-…</c>，
    /// 逐字异或正好是 5。异或是自反的，编码与解码是同一个运算。
    /// </summary>
    public static string Deobfuscate(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            sb.Append((char)(c ^ 5));
        }
        return sb.ToString();
    }



    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return "";
    }

}
