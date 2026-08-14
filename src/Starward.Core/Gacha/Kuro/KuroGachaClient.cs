using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Starward.Core.Gacha.Kuro;

/// <summary>
/// 鸣潮的唤取记录客户端。
/// <para/>
/// 与米哈游的差别有三处：授权 URL 在游戏日志里而不是 WebView 缓存里；
/// 接口是 POST + JSON 而不是带 authkey 的 GET；记录没有服务器端 ID，
/// 因此每次都得整池取回，靠 <see cref="GachaSyntheticId"/> 合成的 ID 去重。
/// </summary>
public class KuroGachaClient : GachaLogClient
{


    /// <summary>
    /// 国服，唤取页面在 aki-game.com
    /// </summary>
    private const string RECORD_API_CN = "https://gmserver-api.aki-game2.com/gacha/record/query";

    /// <summary>
    /// 国际服，唤取页面在 aki-game.net
    /// </summary>
    private const string RECORD_API_GLOBAL = "https://gmserver-api.aki-game2.net/gacha/record/query";


    private const string CONVENE_PAGE_PATH = "/aki/gacha/index.html";

    private static readonly string[] ALLOWED_HOSTS =
    [
        "aki-gm-resources.aki-game.com",
        "aki-gm-resources-oversea.aki-game.com",
        "aki-gm-resources.aki-game.net",
        "aki-gm-resources-oversea.aki-game.net",
    ];


    /// <summary>
    /// 游戏日志里唤取记录 URL 的样子
    /// </summary>
    private static readonly Regex ConveneUrlRegex = new(@"https://aki-gm-resources(?:-oversea)?\.aki-game\.(?:net|com)/aki/gacha/index\.html#/record[^""\s]*", RegexOptions.Compiled);


    private const int PAGE_SIZE = 100;

    /// <summary>
    /// 单个卡池的翻页上限，接口若一直返回同一页，翻页检测会先一步停下
    /// </summary>
    private const int MAX_PAGE_PER_POOL = 500;



    public override IReadOnlyCollection<IGachaType> QueryGachaTypes { get; init; } = new KuroGachaType[] { 1, 2, 3, 4, 5, 6, 7 }.Cast<IGachaType>().ToList().AsReadOnly();



    public KuroGachaClient(HttpClient? httpClient = null) : base(httpClient)
    {

    }



    #region 本机文件


    /// <summary>
    /// 唤取记录 URL 写在游戏日志里。游戏运行时日志是打开的，只能共享读取。
    /// </summary>
    public override string? FindGachaUrlFromLocalFiles(GameBiz gameBiz, string? installPath = null)
    {
        foreach (string file in GetLogFileCandidates(installPath))
        {
            try
            {
                string? url = FindConveneUrlFromFile(file);
                if (url is not null)
                {
                    return url;
                }
            }
            catch (IOException)
            {
                // 某个日志读不到就试下一个
            }
        }
        return null;
    }


    /// <summary>
    /// 可能写有唤取 URL 的日志，按修改时间从新到旧
    /// </summary>
    public static List<string> GetLogFileCandidates(string? installPath)
    {
        var files = new List<string>();
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return files;
        }
        string clientLog = Path.Join(installPath, @"Client\Saved\Logs\Client.log");
        if (File.Exists(clientLog))
        {
            files.Add(clientLog);
        }
        // 内嵌浏览器的日志，国服与国际服放在不同的 KrPcSdk_* 目录下，枚举一遍免得写死
        string thirdParty = Path.Join(installPath, @"Client\Binaries\Win64\ThirdParty");
        if (Directory.Exists(thirdParty))
        {
            foreach (string sdk in Directory.EnumerateDirectories(thirdParty))
            {
                string debugLog = Path.Join(sdk, @"KRSDKRes\KRSDKWebView\debug.log");
                if (File.Exists(debugLog))
                {
                    files.Add(debugLog);
                }
            }
        }
        return files.OrderByDescending(File.GetLastWriteTime).ToList();
    }


    private static string? FindConveneUrlFromFile(string path)
    {
        byte[] bytes = ReadFileWithSharing(path);
        return FindConveneUrl(Encoding.UTF8.GetString(bytes)) ?? FindConveneUrl(DecodeClientLog(bytes));
    }


    /// <summary>
    /// 取最后一个 URL，也就是最近一次在游戏里打开唤取记录时写下的那个
    /// </summary>
    public static string? FindConveneUrl(string text)
    {
        Match? last = null;
        foreach (Match match in ConveneUrlRegex.Matches(text))
        {
            last = match;
        }
        return last?.Value.TrimEnd('\\', ',');
    }


    /// <summary>
    /// Client.log 是异或混淆过的，密钥按字节奇偶决定
    /// </summary>
    public static string DecodeClientLog(ReadOnlySpan<byte> bytes)
    {
        byte[] decoded = new byte[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            decoded[i] = (byte)(bytes[i] ^ ((bytes[i] & 1) != 0 ? 0xA5 : 0xEF));
        }
        return Encoding.UTF8.GetString(decoded);
    }


    #endregion




    #region 授权 URL


    /// <summary>
    /// 唤取 URL 里的账号信息与该用哪个接口
    /// </summary>
    /// <param name="PlayerId">玩家 ID，同时用作 Starward 的 Uid</param>
    public record KuroConveneAuth(string PlayerId, string ServerId, string CardPoolId, string RecordId, string LanguageCode, string RecordApiUrl);


    /// <summary>
    /// 解析唤取 URL。主机不在名单里、或缺少必要参数时抛出。
    /// </summary>
    public static KuroConveneAuth ParseConveneUrl(string conveneUrl)
    {
        if (!Uri.TryCreate(conveneUrl?.Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(CoreLang.Gacha_CannotParseTheWishRecordURL, nameof(conveneUrl));
        }
        string host = uri.Host.ToLowerInvariant();
        if (!ALLOWED_HOSTS.Contains(host) || uri.AbsolutePath != CONVENE_PAGE_PATH)
        {
            throw new ArgumentException(CoreLang.Gacha_CannotParseTheWishRecordURL, nameof(conveneUrl));
        }
        // 参数在 # 之后的查询串里，Uri.Query 拿不到
        string fragment = uri.Fragment.TrimStart('#');
        int index = fragment.IndexOf('?');
        if (index < 0)
        {
            throw new ArgumentException(CoreLang.Gacha_CannotParseTheWishRecordURL, nameof(conveneUrl));
        }
        var query = HttpUtility.ParseQueryString(fragment[(index + 1)..]);
        string Required(string key)
        {
            string? value = query[key];
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException(CoreLang.Gacha_CannotParseTheWishRecordURL, nameof(conveneUrl));
            }
            return value;
        }
        return new KuroConveneAuth(
            PlayerId: Required("player_id"),
            ServerId: Required("svr_id"),
            CardPoolId: Required("resources_id"),
            RecordId: Required("record_id"),
            LanguageCode: query["lang"] is { Length: > 0 } lang ? lang : "zh-Hant",
            RecordApiUrl: host.EndsWith(".net") ? RECORD_API_GLOBAL : RECORD_API_CN);
    }


    protected override string GetGachaUrlPrefix(string gachaUrl, string? lang = null)
    {
        // 鸣潮不用 URL 前缀拼接查询，这里只做校验，让非法 URL 早点报错
        _ = ParseConveneUrl(gachaUrl);
        return gachaUrl;
    }


    /// <summary>
    /// Uid 就写在唤取 URL 里，不必请求接口
    /// </summary>
    public override Task<long> GetUidByGachaUrlAsync(string gachaUrl)
    {
        var auth = ParseConveneUrl(gachaUrl);
        return Task.FromResult(long.TryParse(auth.PlayerId, out long uid) ? uid : 0);
    }


    #endregion




    #region 获取记录


    /// <summary>
    /// 取回全部卡池的记录。
    /// <para/>
    /// <paramref name="endId"/> 与 <paramref name="lang"/> 都用不上：鸣潮的记录没有
    /// 服务器端 ID，无法只取新增的部分；语言由唤取 URL 自己带着，改了接口会拒绝。
    /// </summary>
    public override async Task<IEnumerable<GachaLogItem>> GetGachaLogAsync(string gachaUrl, long endId = 0, string? lang = null, IProgress<(IGachaType GachaType, int Page)>? progress = null, CancellationToken cancellationToken = default)
    {
        var auth = ParseConveneUrl(gachaUrl);
        var result = new List<GachaLogItem>();
        foreach (IGachaType gachaType in QueryGachaTypes)
        {
            result.AddRange(await GetGachaLogByTypeAsync(auth, gachaType, progress, cancellationToken));
        }
        return result;
    }


    public override async Task<IEnumerable<GachaLogItem>> GetGachaLogAsync(string gachaUrl, IGachaType gachaType, long endId = 0, string? lang = null, IProgress<(IGachaType GachaType, int Page)>? progress = null, CancellationToken cancellationToken = default)
    {
        var auth = ParseConveneUrl(gachaUrl);
        return await GetGachaLogByTypeAsync(auth, gachaType, progress, cancellationToken);
    }


    /// <summary>
    /// 鸣潮的接口没有 end_id / page / size 这套查询参数，没有对应实现。
    /// </summary>
    public override Task<IEnumerable<GachaLogItem>> GetGachaLogAsync(string gachaUrl, GachaLogQuery query, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Wuthering Waves does not provide a paged gacha log query API.");
    }


    private async Task<List<KuroGachaItem>> GetGachaLogByTypeAsync(KuroConveneAuth auth, IGachaType gachaType, IProgress<(IGachaType GachaType, int Page)>? progress, CancellationToken cancellationToken)
    {
        var records = new List<KuroGachaRecord>();
        var seenPages = new HashSet<long>();
        for (int page = 1; page <= MAX_PAGE_PER_POOL; page++)
        {
            progress?.Report((gachaType, page));
            List<KuroGachaRecord> list = await QueryPageAsync(auth, gachaType.Value, page, cancellationToken);
            if (list.Count == 0)
            {
                break;
            }
            // 卡池记录不多时接口会忽略页码一直返回同一页，靠内容判断才停得下来
            if (!seenPages.Add(GachaSyntheticId.Fnv1a(string.Join('|', list.Select(x => $"{x.Time}:{x.ResourceId}:{x.Name}")))))
            {
                break;
            }
            records.AddRange(list);
        }
        return ToGachaLogItems(auth, gachaType.Value, records);
    }


    private async Task<List<KuroGachaRecord>> QueryPageAsync(KuroConveneAuth auth, int cardPoolType, int page, CancellationToken cancellationToken)
    {
        await Task.Delay(Random.Shared.Next(200, 300), cancellationToken);
        var body = new KuroGachaQueryBody
        {
            PlayerId = auth.PlayerId,
            ServerId = auth.ServerId,
            CardPoolId = auth.CardPoolId,
            CardPoolType = cardPoolType.ToString(),
            LanguageCode = auth.LanguageCode,
            RecordId = auth.RecordId,
            PageNum = page,
            PageSize = PAGE_SIZE,
        };
        var content = JsonContent.Create(body, KuroGachaJsonContext.Default.KuroGachaQueryBody);
        var response = await _httpClient.PostAsync(auth.RecordApiUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync(typeof(KuroGachaResponse), KuroGachaJsonContext.Default, cancellationToken) as KuroGachaResponse;
        if (result is null)
        {
            return [];
        }
        if (result.Code != 0)
        {
            throw new GachaApiException(result.Code, result.Message ?? CoreLang.Gacha_CannotParseTheWishRecordURL);
        }
        return result.Data ?? [];
    }


    /// <summary>
    /// 把接口记录转成 Starward 的记录。
    /// <para/>
    /// 接口按时间从新到旧返回，这里先转成从旧到新，同一秒内的十连才能拿到
    /// 稳定的序号，合成出来的 ID 也才与游戏内的先后一致。
    /// </summary>
    public static List<KuroGachaItem> ToGachaLogItems(KuroConveneAuth auth, int cardPoolType, List<KuroGachaRecord> records)
    {
        long uid = long.TryParse(auth.PlayerId, out long value) ? value : 0;
        var items = new List<KuroGachaItem>(records.Count);
        var sequences = new Dictionary<DateTime, int>();
        foreach (KuroGachaRecord record in Enumerable.Reverse(records))
        {
            DateTime time = ParseTime(record.Time);
            sequences.TryGetValue(time, out int sequence);
            sequences[time] = sequence + 1;
            items.Add(new KuroGachaItem
            {
                Uid = uid,
                Id = GachaSyntheticId.FromTime(time, sequence, $"{cardPoolType}|{record.ResourceId}|{record.Name}"),
                GachaType = cardPoolType,
                Name = record.Name ?? "",
                ItemType = record.ResourceType ?? "",
                RankType = record.QualityLevel,
                Time = time,
                ItemId = GachaSyntheticId.ToItemId(record.ResourceId.ToString()),
                Count = record.Count == 0 ? 1 : record.Count,
                Lang = auth.LanguageCode,
            });
        }
        return items;
    }


    private static DateTime ParseTime(string? time)
    {
        if (DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value))
        {
            return value;
        }
        return DateTime.MinValue;
    }


    #endregion


}
