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
    /// 官方新启动器（3.0.x，支持资源分级）换用的 AppKey，appId 不变。
    /// 它自己的配置在 <c>launcher/app/{APP_ID}_{OFFICIAL_APP_KEY}/index.json</c>，
    /// 游戏配置见 <see cref="GetOfficialGameIndexAsync"/>。取自 GitHub 上 JLLSS/WuWa-Resource-Data 记下的地址。
    /// </summary>
    private const string OFFICIAL_APP_KEY = "P7xcUZnEr1AXIGON25E6KjpOgTlVrg6e";


    /// <summary>
    /// 背景图这一路比其他配置多一段令牌，与账号和登录状态无关。
    /// <para/>
    /// 令牌不是固定的：官方启动器每次从自身配置的 <c>functionCode.background</c> 读，
    /// 并随游戏版本轮换。旧令牌的路径不会下架，而是一直返回旧版本的美术，
    /// 所以写死令牌不会报错，只会让背景停在旧版本上（3.7.0 就是这样）。
    /// 这里的值只在取不到启动器配置时兜底用。
    /// </summary>
    private const string FALLBACK_BACKGROUND_TOKEN = "lv1emIbKHn38mW6zgxiFqU3Uw8nwstj6";


    /// <summary>
    /// 官方启动器支持的语言，取自 <c>supportLanguagesArray</c>。
    /// 不在其中的语言会退回 <see cref="DEFAULT_LANGUAGE"/>，与官方启动器一致。
    /// </summary>
    private const string DEFAULT_LANGUAGE = "en";


    /// <summary>
    /// 主 CDN 在前，备援在后
    /// </summary>
    private static readonly string[] Hosts = [CDN_PRIMARY, CDN_BACKUP];


    /// <summary>
    /// 游戏内公告的 CDN，主站在前。游戏 SDK 初始化公告时给的是第一个，
    /// 后两个取自清单里每则公告的 <c>contentPrefix</c>，同一份清单三处都有。
    /// </summary>
    private static readonly string[] NoticeHosts =
    [
        "https://aki-gm-resources-back.aki-game.net",
        "https://aki-gm-resources-back-aws.aki-game.net",
        "https://aki-gm-res-back-akamai.aki-game.net",
    ];


    /// <summary>
    /// 国际服游戏内公告的服务器标识，取自游戏 SDK 初始化公告网页时的 <c>serverId</c>
    /// </summary>
    private const string NOTICE_SERVER_ID = "6eb2a235b30d05efd77bedb5cf60999e";


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
        string token = await GetBackgroundTokenAsync(cancellationToken);
        return await GetFromCdnAsync<KuroLauncherBackground>(
            host => $"{host}/launcher/{APP_ID}_{APP_KEY}/{GAME_ID}/background/{token}/{language}.json",
            cancellationToken);
    }


    /// <summary>
    /// 当前版本的背景图令牌，取不到启动器配置时退回内置值。
    /// </summary>
    private async Task<string> GetBackgroundTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            KuroLauncherConfig? config = await GetLauncherConfigAsync(cancellationToken);
            string? token = config?.FunctionCode?.Background;
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 配置取不到时内置令牌至少还能给出一张（可能是旧版本的）背景
        }
        return FALLBACK_BACKGROUND_TOKEN;
    }


    /// <summary>
    /// 官方启动器自身的配置，两个 CDN 都取不到时返回 null。
    /// <para/>
    /// 路径是 <c>launcher/launcher/{APP_ID}_{APP_KEY}/{GAME_ID}</c>，多一段 <c>launcher</c>，
    /// 与游戏配置 <see cref="GetGameIndexAsync"/> 是两份不同的文件。
    /// </summary>
    public async Task<KuroLauncherConfig?> GetLauncherConfigAsync(CancellationToken cancellationToken = default)
    {
        return await GetFromCdnAsync<KuroLauncherConfig>(
            host => $"{host}/launcher/launcher/{APP_ID}_{APP_KEY}/{GAME_ID}/index.json",
            cancellationToken);
    }


    /// <summary>
    /// 首页的轮播图与资讯，两个 CDN 都取不到时返回 null。
    /// <para/>
    /// 与背景图同一族路径，按语言分文件。
    /// </summary>
    /// <param name="language">官方启动器的语言代码，见 <see cref="GetLanguageCode"/></param>
    public async Task<KuroLauncherInformation?> GetInformationAsync(string language, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            language = DEFAULT_LANGUAGE;
        }
        return await GetFromCdnAsync<KuroLauncherInformation>(
            host => $"{host}/launcher/{APP_ID}_{APP_KEY}/{GAME_ID}/information/{language}.json",
            cancellationToken);
    }


    /// <summary>
    /// 游戏配置，其中包含线上的游戏版本号。两个 CDN 都取不到时返回 null。
    /// <para/>
    /// 路径与背景图那一条不同：这里是 <c>launcher/game/{GAME_ID}/{APP_ID}_{APP_KEY}</c>，
    /// 背景图是 <c>launcher/{APP_ID}_{APP_KEY}/{GAME_ID}</c>，两段的顺序正好相反，
    /// 都取自官方启动器自己请求过的地址，不能互相套用。
    /// </summary>
    public async Task<KuroLauncherGameIndex?> GetGameIndexAsync(CancellationToken cancellationToken = default)
    {
        return await GetFromCdnAsync<KuroLauncherGameIndex>(
            host => $"{host}/launcher/game/{GAME_ID}/{APP_ID}_{APP_KEY}/index.json",
            cancellationToken);
    }


    /// <summary>
    /// 新启动器的分级游戏配置，两个 CDN 都取不到时返回 null。
    /// <para/>
    /// 路径又是另一种排法：<c>launcher/game/{APP_ID}_{AppKey}/{GAME_ID}/official/index.json</c>，
    /// 而且要用新的 AppKey，拿旧的 AppKey 套这个路径是 404。
    /// </summary>
    public async Task<KuroOfficialGameIndex?> GetOfficialGameIndexAsync(CancellationToken cancellationToken = default)
    {
        return await GetFromCdnAsync<KuroOfficialGameIndex>(
            host => $"{host}/launcher/game/{APP_ID}_{OFFICIAL_APP_KEY}/{GAME_ID}/official/index.json",
            cancellationToken);
    }


    /// <summary>
    /// 文件清单（indexFile.json / resource.json）。
    /// <para/>
    /// 清单放在下载 CDN 上，不在取配置的 CDN 上：<paramref name="cdnBases"/> 取自
    /// <see cref="KuroLauncherGameResource.CdnList"/>，见 <see cref="KuroDownloadPlanner.GetCdnBases"/>。
    /// </summary>
    /// <param name="relativePath">清单相对于 CDN 根目录的路径，例如 <see cref="KuroLauncherGameConfig.IndexFile"/></param>
    public async Task<KuroResourceIndex?> GetResourceIndexAsync(IReadOnlyList<string> cdnBases, string relativePath, CancellationToken cancellationToken = default)
    {
        return await GetFromCdnAsync<KuroResourceIndex>(cdnBases, host => KuroDownloadPlanner.Combine(host, relativePath), cancellationToken);
    }


    /// <summary>
    /// 游戏内公告的清单。与启动器配置不在同一组 CDN 上。
    /// </summary>
    public async Task<KuroGameNoticeList?> GetGameNoticeListAsync(CancellationToken cancellationToken = default)
    {
        return await GetFromCdnAsync<KuroGameNoticeList>(NoticeHosts,
            host => $"{host}/gamenotice/{GAME_ID}/{NOTICE_SERVER_ID}/notice.json",
            cancellationToken);
    }


    /// <summary>
    /// 一则游戏内公告的正文。这种语言没有时退回英文。
    /// </summary>
    /// <param name="contentPrefixes">取自 <see cref="KuroGameNotice.ContentPrefix"/>，主站在前</param>
    /// <param name="language">官方启动器的语言代码，见 <see cref="GetLanguageCode"/></param>
    public async Task<KuroGameNoticeContent?> GetGameNoticeContentAsync(IReadOnlyList<string> contentPrefixes, string language, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            language = DEFAULT_LANGUAGE;
        }
        try
        {
            return await GetFromCdnAsync<KuroGameNoticeContent>(contentPrefixes, prefix => $"{prefix.TrimEnd('/')}/{language}.json", cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound && language != DEFAULT_LANGUAGE)
        {
            return await GetFromCdnAsync<KuroGameNoticeContent>(contentPrefixes, prefix => $"{prefix.TrimEnd('/')}/{DEFAULT_LANGUAGE}.json", cancellationToken);
        }
    }


    /// <summary>
    /// 依次尝试每个 CDN。主 CDN 挂掉时安静地换备援，两个都不行才让调用方知道。
    /// </summary>
    private Task<T?> GetFromCdnAsync<T>(Func<string, string> urlFactory, CancellationToken cancellationToken) where T : class
    {
        return GetFromCdnAsync<T>(Hosts, urlFactory, cancellationToken);
    }


    private async Task<T?> GetFromCdnAsync<T>(IReadOnlyList<string> hosts, Func<string, string> urlFactory, CancellationToken cancellationToken) where T : class
    {
        Exception? lastException = null;
        foreach (string host in hosts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string url = urlFactory(host);
                return await _httpClient.GetFromJsonAsync(url, typeof(T), KuroLauncherJsonContext.Default, cancellationToken) as T;
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
