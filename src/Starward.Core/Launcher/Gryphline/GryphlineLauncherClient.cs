using System.Globalization;
using System.Net;
using System.Net.Http.Json;

namespace Starward.Core.Launcher.Gryphline;

/// <summary>
/// GRYPHLINK 官方启动器的在线配置客户端。
/// <para/>
/// 与米哈游、库洛的差别是这家不放静态 JSON，而是一支聚合接口：
/// 把想问的东西按 <c>kind</c> 列进 <c>proxy_reqs</c>，一次问完。
/// 不需要登录，官方启动器预取背景图时也是不带令牌的。
/// </summary>
public class GryphlineLauncherClient
{


    private const string API_BATCH_PROXY = "https://launcher.gryphline.com/api/proxy/web/batch_proxy";


    public const string KIND_MAIN_BG_IMAGE = "get_main_bg_image";


    /// <summary>
    /// 终末地的游戏标识，取自官方启动器网页前端的地址栏参数 <c>app_code</c>。
    /// <para/>
    /// 它是一段不透明的令牌，不是游戏名字：传 <c>endfield</c> 这类名字接口会返回空结果。
    /// </summary>
    public const string ENDFIELD_APP_CODE = "YDUTE5gscDZ229CW";


    /// <summary>
    /// 官方启动器支持的语言。接口用的是完整的地区代码，
    /// 与库洛的脚本写法（zh-Hant）不同。
    /// </summary>
    private const string DEFAULT_LANGUAGE = "en-us";


    private readonly HttpClient _httpClient;


    public GryphlineLauncherClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
    }



    /// <summary>
    /// 把系统界面语言换成接口认识的语言代码。
    /// <para/>
    /// 终末地每种语言的背景图和视频都不一样——文案是画进美术里的，
    /// 因此语言选错拿到的是另一张图，而不只是另一行标语。
    /// </summary>
    public static string GetLanguageCode(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        if (culture.TwoLetterISOLanguageName is "zh")
        {
            string name = culture.Name;
            if (name.Contains("Hant", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-tw";
            }
            if (name.Contains("Hans", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-cn";
            }
            return name switch
            {
                "zh-TW" or "zh-HK" or "zh-MO" => "zh-tw",
                _ => "zh-cn",
            };
        }
        return culture.TwoLetterISOLanguageName switch
        {
            "en" => "en-us",
            "ja" => "ja-jp",
            "ko" => "ko-kr",
            "ru" => "ru-ru",
            "de" => "de-de",
            "fr" => "fr-fr",
            "th" => "th-th",
            "vi" => "vi-vn",
            "id" => "id-id",
            "it" => "it-it",
            "pt" => "pt-br",
            "es" => "es-es",
            _ => DEFAULT_LANGUAGE,
        };
    }



    /// <summary>
    /// 首页背景图，没有结果时返回 null。
    /// </summary>
    /// <param name="appCode">游戏标识，见 <see cref="ENDFIELD_APP_CODE"/></param>
    /// <param name="language">完整地区语言代码，见 <see cref="GetLanguageCode"/></param>
    public async Task<GryphlineMainBgImage?> GetMainBackgroundImageAsync(string appCode, string language, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            language = DEFAULT_LANGUAGE;
        }
        var request = new GryphlineBatchProxyRequest
        {
            ProxyRequests =
            [
                new GryphlineProxyRequest
                {
                    Kind = KIND_MAIN_BG_IMAGE,
                    MainBgImageRequest = new GryphlineMainBgImageRequest
                    {
                        AppCode = appCode,
                        Language = language,
                        // 渠道留空。填对了（本机是 6）与留空得到的素材相同，
                        // 但渠道是随安装来源变的（官方、Epic、Steam），
                        // 写死一个值反而会在别的安装上问不到东西。
                        Channel = "",
                        SubChannel = "",
                    },
                },
            ],
        };
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync(API_BATCH_PROXY, request, GryphlineLauncherJsonContext.Default.GryphlineBatchProxyRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync(typeof(GryphlineBatchProxyResponse), GryphlineLauncherJsonContext.Default, cancellationToken) as GryphlineBatchProxyResponse;
        return result?.ProxyResponses?.FirstOrDefault(x => x.Kind == KIND_MAIN_BG_IMAGE)?.MainBgImageResponse?.MainBgImage;
    }


}
