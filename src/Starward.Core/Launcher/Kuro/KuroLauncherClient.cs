using System.Globalization;
using System.Net;
using System.Net.Http.Json;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 鸣潮官方启动器的在线配置客户端。
/// <para/>
/// 与米哈游的 <see cref="HoYoPlay.HoYoPlayClient"/> 是同一类东西：官方启动器把首页要显示的内容
/// 放在 CDN 上的静态 JSON 里，不需要登录就能取。区别有两处：米哈游一支
/// <c>getAllGameBasicInfo</c> 就把所有游戏的背景图一起返回，鸣潮是一个渠道一种语言各一份文件；
/// 米哈游返回的每张背景图都带服务器端 ID，鸣潮没有。
/// </summary>
public class KuroLauncherClient
{


    /// <summary>
    /// 主 CDN。与官方启动器 <c>configUrl</c> 用的是同一个域名。
    /// </summary>
    private const string CDN_PRIMARY = "https://prod-alicdn-gamestarter.kurogame.com";

    /// <summary>
    /// 备援 CDN，对应官方启动器的 <c>backUpConfigUrl</c>
    /// </summary>
    private const string CDN_BACKUP = "https://prod-volcdn-gamestarter.kurogame.net";


    /// <summary>
    /// 国际服的三段标识，取自官方启动器发给网页前端的 <c>kr_get_launcher_conf</c>。
    /// 国服是另一套 appId，本项目暂时只支持国际服。
    /// </summary>
    private const string GAME_ID = "G153";
    private const string APP_ID = "50004";
    private const string APP_KEY = "obOHXFrFanqsaIEOmuKroCcbZkQRBC7c";


    /// <summary>
    /// 背景图这一路比其他配置多一段固定令牌，写死在官方启动器的网页前端里，
    /// 与账号和登录状态无关。
    /// <para/>
    /// 官方替换前端时它有可能跟着换，那时接口会返回 404，
    /// 调用方按「拿不到在线背景」处理即可，不影响其他功能。
    /// </summary>
    private const string BACKGROUND_TOKEN = "nmJutnA7saYMz2eJ46CL8mB3VUEZvyCs";


    /// <summary>
    /// 官方启动器支持的语言，取自 <c>supportLanguagesArray</c>。
    /// 不在其中的语言会退回 <see cref="DEFAULT_LANGUAGE"/>，与官方启动器一致。
    /// </summary>
    private const string DEFAULT_LANGUAGE = "en";


    /// <summary>
    /// 主 CDN 在前，备援在后
    /// </summary>
    private static readonly string[] Hosts = [CDN_PRIMARY, CDN_BACKUP];


    private readonly HttpClient _httpClient;


    public KuroLauncherClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
    }



    /// <summary>
    /// 把系统界面语言换成官方启动器认识的语言代码。
    /// <para/>
    /// 不能直接用 <see cref="CultureInfo.Name"/>：官方用的是 <c>zh-Hans</c> / <c>zh-Hant</c>
    /// 这种脚本写法，而系统给的是 <c>zh-CN</c> / <c>zh-TW</c>。
    /// </summary>
    public static string GetLanguageCode(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        if (culture.TwoLetterISOLanguageName is "zh")
        {
            // 繁简之分看脚本，取不到脚本时按地区判断
            string name = culture.Name;
            if (name.Contains("Hant", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-Hant";
            }
            if (name.Contains("Hans", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-Hans";
            }
            return name switch
            {
                "zh-TW" or "zh-HK" or "zh-MO" => "zh-Hant",
                _ => "zh-Hans",
            };
        }
        return culture.TwoLetterISOLanguageName switch
        {
            "en" => "en",
            "ja" => "ja",
            "ko" => "ko",
            "fr" => "fr",
            "de" => "de",
            "es" => "es",
            "th" => "th",
            // 官方只有巴西葡语一种葡语
            "pt" => "pt-BR",
            _ => DEFAULT_LANGUAGE,
        };
    }



    /// <summary>
    /// 首页背景图配置，两个 CDN 都取不到时返回 null。
    /// </summary>
    /// <param name="language">官方启动器的语言代码，见 <see cref="GetLanguageCode"/></param>
    public async Task<KuroLauncherBackground?> GetBackgroundAsync(string language, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            language = DEFAULT_LANGUAGE;
        }
        // 背景图不是关键功能，主 CDN 挂掉时安静地换备援，
        // 两个都不行才让调用方知道
        Exception? lastException = null;
        foreach (string host in Hosts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string url = $"{host}/launcher/{APP_ID}_{APP_KEY}/{GAME_ID}/background/{BACKGROUND_TOKEN}/{language}.json";
                return await _httpClient.GetFromJsonAsync(url, typeof(KuroLauncherBackground), KuroLauncherJsonContext.Default, cancellationToken) as KuroLauncherBackground;
            }
            // 只有调用方真的取消了才立刻退出。HttpClient 自己超时抛的也是
            // OperationCanceledException，那种情况该继续试备援。
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }
        if (lastException is not null)
        {
            throw lastException;
        }
        return null;
    }


}
