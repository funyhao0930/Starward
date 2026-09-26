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


    /// <summary>
    /// 游戏包那一族走的是不带 web 的聚合接口，与背景、公告不同
    /// </summary>
    private const string API_GAME_BATCH_PROXY = "https://launcher.gryphline.com/api/proxy/batch_proxy";


    public const string KIND_LATEST_GAME = "get_latest_game";


    /// <summary>
    /// 国际服 GRYPHLINK 启动器自己的标识。
    /// 启动器的构建路径里就写着它（<c>publish-launcher-184\TiaytKBUIEdoEwRT\1.6.0.1607</c>）。
    /// </summary>
    public const string GLOBAL_LAUNCHER_APP_CODE = "TiaytKBUIEdoEwRT";


    /// <summary>
    /// 国际服官方渠道。
    /// <para/>
    /// 查游戏包时渠道必须填对，留空或填错都拿不到结果。本机的渠道记在加密的日志里，
    /// 读不出来，因此只能按官方渠道问：Epic、Steam 等其他来源的安装会问不到，
    /// 这时安静地不提示，而不是报一个不属于它的版本。
    /// </summary>
    public const string GLOBAL_OFFICIAL_CHANNEL = "6";


    public const string KIND_MAIN_BG_IMAGE = "get_main_bg_image";

    public const string KIND_BANNER = "get_banner";

    public const string KIND_ANNOUNCEMENT = "get_announcement";


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
                    MainBgImageRequest = MakeRequest(appCode, language),
                },
            ],
        };
        GryphlineBatchProxyResponse? result = await PostAsync(request, cancellationToken);
        return Find(result, KIND_MAIN_BG_IMAGE)?.MainBgImageResponse?.MainBgImage;
    }


    /// <summary>
    /// 首页的轮播图与公告，一次问完。
    /// <para/>
    /// 这正是聚合接口的用处：官方启动器自己也是把首页要的东西并成一个请求。
    /// 任一项没有结果时对应的返回为 null，调用方按「这一块没有内容」处理。
    /// </summary>
    /// <param name="appCode">游戏标识，见 <see cref="ENDFIELD_APP_CODE"/></param>
    /// <param name="language">完整地区语言代码，见 <see cref="GetLanguageCode"/></param>
    public async Task<(GryphlineBannerResponse? Banners, GryphlineAnnouncementResponse? Announcements)> GetLauncherContentAsync(
        string appCode, string language, CancellationToken cancellationToken = default)
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
                    Kind = KIND_BANNER,
                    BannerRequest = MakeRequest(appCode, language),
                },
                new GryphlineProxyRequest
                {
                    Kind = KIND_ANNOUNCEMENT,
                    AnnouncementRequest = MakeRequest(appCode, language),
                },
            ],
        };
        GryphlineBatchProxyResponse? result = await PostAsync(request, cancellationToken);
        return (Find(result, KIND_BANNER)?.BannerResponse, Find(result, KIND_ANNOUNCEMENT)?.AnnouncementResponse);
    }


    /// <summary>
    /// 最新游戏包，问不到时返回 null。
    /// <para/>
    /// 渠道填错时接口不报错，只是 proxy_rsps 整个缺席，这里同样按「问不到」返回 null。
    /// </summary>
    /// <param name="appCode">游戏标识，见 <see cref="ENDFIELD_APP_CODE"/></param>
    public async Task<GryphlineLatestGame?> GetLatestGameAsync(string appCode, CancellationToken cancellationToken = default)
    {
        var request = new GryphlineBatchProxyRequest
        {
            ProxyRequests =
            [
                new GryphlineProxyRequest
                {
                    Kind = KIND_LATEST_GAME,
                    LatestGameRequest = new GryphlineLatestGameRequest
                    {
                        AppCode = appCode,
                        LauncherAppCode = GLOBAL_LAUNCHER_APP_CODE,
                        Channel = GLOBAL_OFFICIAL_CHANNEL,
                        SubChannel = GLOBAL_OFFICIAL_CHANNEL,
                    },
                },
            ],
        };
        GryphlineBatchProxyResponse? result = await PostAsync(API_GAME_BATCH_PROXY, request, cancellationToken);
        return Find(result, KIND_LATEST_GAME)?.LatestGameResponse;
    }


    /// <summary>
    /// 每个 kind 的请求体都一样，只有 appcode 与语言会变
    /// </summary>
    private static GryphlineLauncherRequest MakeRequest(string appCode, string language)
    {
        return new GryphlineLauncherRequest
        {
            AppCode = appCode,
            Language = language,
            // 渠道留空。填对了（本机是 6）与留空得到的素材相同，
            // 但渠道是随安装来源变的（官方、Epic、Steam），
            // 写死一个值反而会在别的安装上问不到东西。
            Channel = "",
            SubChannel = "",
        };
    }


    private Task<GryphlineBatchProxyResponse?> PostAsync(GryphlineBatchProxyRequest request, CancellationToken cancellationToken)
    {
        return PostAsync(API_BATCH_PROXY, request, cancellationToken);
    }


    private async Task<GryphlineBatchProxyResponse?> PostAsync(string url, GryphlineBatchProxyRequest request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await _httpClient.PostAsJsonAsync(url, request, GryphlineLauncherJsonContext.Default.GryphlineBatchProxyRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(typeof(GryphlineBatchProxyResponse), GryphlineLauncherJsonContext.Default, cancellationToken) as GryphlineBatchProxyResponse;
    }


    private static GryphlineProxyResponse? Find(GryphlineBatchProxyResponse? response, string kind)
    {
        return response?.ProxyResponses?.FirstOrDefault(x => x.Kind == kind);
    }


}
