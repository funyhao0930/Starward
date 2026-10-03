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
    /// 游戏内公告的接口，与启动器不在同一个域名上
    /// </summary>
    private const string API_BULLETIN = "https://game-hub.gryphline.com/bulletin/v2/aggregate";


    /// <summary>
    /// 国际服游戏内公告的标识。国服是另一个（<c>endfield_5SD9TN</c>，域名也不同）。
    /// </summary>
    private const string ENDFIELD_BULLETIN_CODE = "endfield_U35PW8";


    /// <summary>
    /// 国际服亚洲服的公告分组。两组的活动时间按各自的时区写（UTC+8 与 UTC-5），
    /// 版本说明也是两篇，不能混用。
    /// </summary>
    public const string BULLETIN_SERVER_ASIA = "2";

    /// <summary>
    /// 国际服美洲 / 欧洲服的公告分组
    /// </summary>
    public const string BULLETIN_SERVER_AMERICAS_EUROPE = "3";


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


    /// <summary>
    /// 读分卷片段的读取超时，默认 <see cref="IdleTimeoutStream.DefaultTimeout"/>。测试会调短
    /// </summary>
    internal TimeSpan? PackageReadTimeout { get; init; }


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
    /// 游戏内公告，连正文一起。接口回报失败时抛出。
    /// <para/>
    /// 与启动器首页的公告（<see cref="KIND_ANNOUNCEMENT"/>）不是同一份：那边只有标题和外链，
    /// 这边是游戏里那块公告窗口的内容。渠道留默认（<c>#DEFAULT</c>）就是官方渠道的公告。
    /// </summary>
    /// <param name="language">完整地区语言代码，见 <see cref="GetLanguageCode"/></param>
    /// <param name="server">公告分组，见 <see cref="BULLETIN_SERVER_ASIA"/> 与 <see cref="GetBulletinServer"/></param>
    public async Task<GryphlineBulletinData?> GetBulletinAsync(string language, string server, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            language = DEFAULT_LANGUAGE;
        }
        string url = $"{API_BULLETIN}?lang={Uri.EscapeDataString(language)}&platform=Windows&channel={GLOBAL_OFFICIAL_CHANNEL}&type=0"
                   + $"&code={ENDFIELD_BULLETIN_CODE}&hideDetail=0&server={Uri.EscapeDataString(server)}";
        var response = await _httpClient.GetFromJsonAsync(url, typeof(GryphlineBulletinResponse), GryphlineLauncherJsonContext.Default, cancellationToken) as GryphlineBulletinResponse;
        if (response is null)
        {
            return null;
        }
        if (response.Code is not 0)
        {
            throw new HttpRequestException($"Endfield bulletin returned {response.Code}: {response.Message}");
        }
        return response.Data;
    }


    /// <summary>
    /// 游戏内公告窗口打开的官方网页，与游戏里显示的完全相同。
    /// <para/>
    /// 地址取自游戏内建浏览器的磁盘缓存（<c>%LOCALAPPDATA%\PlatformProcess\Cache</c>），
    /// 游戏还会带上登录用的 <c>u8_token</c>，但公告不需要登录，不带也能完整显示。
    /// 登录前的那一版是 <c>gate_bulletin</c>。
    /// </summary>
    /// <param name="language">完整地区语言代码，见 <see cref="GetLanguageCode"/></param>
    /// <param name="server">公告分组，见 <see cref="GetBulletinServer"/></param>
    public static string GetBulletinPageUrl(string language, string server)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            language = DEFAULT_LANGUAGE;
        }
        return $"{BULLETIN_PAGE}?platform=Windows&channel={GLOBAL_OFFICIAL_CHANNEL}&subChannel={GLOBAL_OFFICIAL_CHANNEL}"
             + $"&lang={Uri.EscapeDataString(language)}&server={Uri.EscapeDataString(server)}";
    }


    /// <summary>
    /// 游戏内公告网页
    /// </summary>
    public const string BULLETIN_PAGE = "https://ef-webview.gryphline.com/page/game_bulletin";


    /// <summary>
    /// 按本机时区猜玩家在哪一服。
    /// <para/>
    /// 玩的是哪一服只有登录后的角色才知道，而公告不需要登录；亚洲服写的是 UTC+8，
    /// 美洲 / 欧洲服写的是 UTC-5，以 UTC+4 为界分开，与玩家按地区选服的习惯一致。
    /// </summary>
    public static string GetBulletinServer(TimeSpan utcOffset)
    {
        return utcOffset >= TimeSpan.FromHours(4) ? BULLETIN_SERVER_ASIA : BULLETIN_SERVER_AMERICAS_EUROPE;
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
    /// 整包里的全部条目（含目录），读自最后一卷末尾的 zip 中央目录。
    /// <para/>
    /// 只下载两小段：末尾不到 64 KB 找出中央目录的位置，再读中央目录本身（1.5.3 是 260 KB）。
    /// 分卷是一个 zip 直接切开的，所以偏移要按各卷首尾相接计算。
    /// </summary>
    /// <param name="package">需要带分卷列表：请求时版本号留空才会有，见 <see cref="GetLatestGameAsync"/></param>
    public async Task<IReadOnlyList<ZipEntryInfo>> GetPackageEntriesAsync(GryphlineGamePackage package, CancellationToken cancellationToken = default)
    {
        List<GryphlinePackageFile> packs = package.Packs ?? [];
        if (packs.Count == 0 || packs.Any(x => string.IsNullOrWhiteSpace(x.Url) || x.Size <= 0))
        {
            throw new InvalidOperationException("The package has no pack list.");
        }
        List<long> sizes = packs.Select(x => x.Size).ToList();
        long total = sizes.Sum();
        long tailLength = Math.Min(ZipCentralDirectory.MaxTailSize, total);
        long tailStart = total - tailLength;
        byte[] tail = await ReadPackageRangeAsync(packs, sizes, tailStart, tailLength, cancellationToken);
        ZipCentralDirectoryInfo info = ZipCentralDirectory.Locate(tail, tailStart);
        byte[] centralDirectory = info.Offset >= tailStart && info.Offset + info.Size <= total
                                ? tail.AsSpan((int)(info.Offset - tailStart), (int)info.Size).ToArray()
                                : await ReadPackageRangeAsync(packs, sizes, info.Offset, info.Size, cancellationToken);
        IReadOnlyList<ZipEntryInfo> entries = ZipCentralDirectory.ReadEntries(centralDirectory);
        if (entries.Count != info.EntryCount)
        {
            throw new InvalidDataException($"Central directory entry count mismatch: {entries.Count} != {info.EntryCount}");
        }
        return entries;
    }


    private async Task<byte[]> ReadPackageRangeAsync(List<GryphlinePackageFile> packs, List<long> sizes, long start, long length, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream((int)length);
        foreach ((int index, long offset, long count) in GryphlineDownloadPlanner.MapRange(sizes, start, length))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, packs[index].Url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + count - 1);
            using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            // 不用 ReadAsByteArrayAsync：ResponseHeadersRead 之后 HttpClient.Timeout 不管读正文，
            // 连接悄悄断掉的话会一直等下去，安装停在准备阶段，所以自己读流并计时
            using var body = new MemoryStream();
            using (var stream = new IdleTimeoutStream(await response.Content.ReadAsStreamAsync(cancellationToken), PackageReadTimeout))
            {
                await stream.CopyToAsync(body, cancellationToken);
            }
            Memory<byte> bytes = body.GetBuffer().AsMemory(0, (int)body.Length);
            // CDN 不认 Range 时会回整个分卷（200），只取要的那一段
            if (response.StatusCode is HttpStatusCode.OK && bytes.Length >= offset + count)
            {
                bytes = bytes.Slice((int)offset, (int)count);
            }
            if (bytes.Length != count)
            {
                throw new InvalidDataException($"Range read returned {bytes.Length} bytes, expected {count}.");
            }
            ms.Write(bytes.Span);
        }
        return ms.ToArray();
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
