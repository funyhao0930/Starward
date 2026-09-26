using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Starward.Core.Gacha.Gryphline;

/// <summary>
/// 明日方舟：终末地的寻访记录客户端。
/// <para/>
/// 授权 URL 不在游戏目录里，而在启动器进程的公共缓存
/// <c>%LOCALAPPDATA%\PlatformProcess\Cache\data_1</c>；
/// 接口是普通的 GET，按 <c>seq_id</c> 游标从新往旧翻页。
/// </summary>
public class GryphlineGachaClient : GachaLogClient
{


    private static readonly string[] ALLOWED_HOSTS =
    [
        "ef-webview.gryphline.com",
        "ef-webview.hypergryph.com",
    ];


    private const string CHARACTER_RECORD_PATH = "/api/record/char";

    private const string WEAPON_RECORD_PATH = "/api/record/weapon";


    /// <summary>
    /// 缓存里寻访记录 URL 的样子
    /// </summary>
    private static readonly Regex RecordUrlRegex = new(@"https://ef-webview\.(?:gryphline|hypergryph)\.com/api/record/(?:char|weapon)\?[^\x00\s""'<>]+", RegexOptions.Compiled);


    /// <summary>
    /// 单个卡池的翻页上限，正常情况下 hasMore 会先变成 false
    /// </summary>
    private const int MAX_PAGE_PER_POOL = 500;



    public override IReadOnlyCollection<IGachaType> QueryGachaTypes { get; init; } = new GryphlineGachaType[] { 1, 2, 3, 4, 5 }.Cast<IGachaType>().ToList().AsReadOnly();



    public GryphlineGachaClient(HttpClient? httpClient = null) : base(httpClient)
    {

    }



    #region 本机文件


    /// <summary>
    /// 启动器进程的缓存文件，游戏运行时是打开的
    /// </summary>
    public static string GetRecordCacheFilePath()
    {
        return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"PlatformProcess\Cache\data_1");
    }


    /// <summary>
    /// 寻访 URL 与游戏安装目录无关，<paramref name="installPath"/> 用不上。
    /// </summary>
    public override string? FindGachaUrlFromLocalFiles(GameBiz gameBiz, string? installPath = null)
    {
        string file = GetRecordCacheFilePath();
        if (!File.Exists(file))
        {
            return null;
        }
        try
        {
            return FindRecordUrl(Encoding.UTF8.GetString(ReadFileWithSharing(file)));
        }
        catch (IOException)
        {
            return null;
        }
    }


    /// <summary>
    /// 缓存里会留下多次访问的痕迹，从最新的往回找第一个参数齐全的。
    /// </summary>
    public static string? FindRecordUrl(string text)
    {
        var matches = RecordUrlRegex.Matches(text);
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            string candidate = matches[i].Value.TrimEnd('\\', '}', ']');
            if (TryParseRecordUrl(candidate, out _))
            {
                return candidate;
            }
        }
        return null;
    }


    #endregion




    #region 授权 URL


    /// <summary>
    /// 寻访 URL 里的账号信息
    /// </summary>
    public record GryphlineRecordAuth(string Host, string Token, string ServerId, string Language);


    public static bool TryParseRecordUrl(string recordUrl, out GryphlineRecordAuth? auth)
    {
        try
        {
            auth = ParseRecordUrl(recordUrl);
            return true;
        }
        catch (ArgumentException)
        {
            auth = null;
            return false;
        }
    }


    /// <summary>
    /// 解析寻访 URL。主机不在名单里、或没有 token 时抛出。
    /// </summary>
    public static GryphlineRecordAuth ParseRecordUrl(string recordUrl)
    {
        if (!Uri.TryCreate(recordUrl?.Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(CoreLang.Gacha_CannotParseTheWishRecordURL, nameof(recordUrl));
        }
        string host = uri.Host.ToLowerInvariant();
        if (!ALLOWED_HOSTS.Contains(host) || !uri.AbsolutePath.StartsWith("/api/record/"))
        {
            throw new ArgumentException(CoreLang.Gacha_CannotParseTheWishRecordURL, nameof(recordUrl));
        }
        var query = HttpUtility.ParseQueryString(uri.Query);
        string? token = query["token"];
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentException(CoreLang.Gacha_CannotParseTheWishRecordURL, nameof(recordUrl));
        }
        return new GryphlineRecordAuth(
            Host: host,
            Token: token,
            ServerId: query["server_id"] is { Length: > 0 } server ? server : "1",
            Language: query["lang"] is { Length: > 0 } lang ? lang : "zh-tw");
    }


    protected override string GetGachaUrlPrefix(string gachaUrl, string? lang = null)
    {
        // 终末地按卡池分别拼请求，没有公共前缀，这里只做校验
        _ = ParseRecordUrl(gachaUrl);
        return gachaUrl;
    }


    /// <summary>
    /// 寻访 URL 里没有玩家 ID，只有 token 与区服。
    /// token 每次登录都会变，不能当身份用，因此以区服号作为 Uid；
    /// 同一区服的多个账号会被并在一起，这是接口没给出账号标识的结果。
    /// </summary>
    public override Task<long> GetUidByGachaUrlAsync(string gachaUrl)
    {
        var auth = ParseRecordUrl(gachaUrl);
        return Task.FromResult(long.TryParse(auth.ServerId, out long uid) ? uid : 0);
    }


    #endregion




    #region 获取记录


    /// <summary>
    /// 取回全部卡池的记录。
    /// <para/>
    /// <paramref name="endId"/> 用不上：接口的 <c>seq_id</c> 游标只能往更早翻，
    /// 没有「只取比某条更新的记录」的用法，因此每次都整池取回，靠 seqId 去重。
    /// </summary>
    public override async Task<IEnumerable<GachaLogItem>> GetGachaLogAsync(string gachaUrl, long endId = 0, string? lang = null, IProgress<(IGachaType GachaType, int Page)>? progress = null, CancellationToken cancellationToken = default)
    {
        var auth = ParseRecordUrl(gachaUrl);
        var result = new List<GachaLogItem>();
        foreach (IGachaType gachaType in QueryGachaTypes)
        {
            result.AddRange(await GetGachaLogByTypeAsync(auth, gachaType, progress, cancellationToken));
        }
        return result;
    }


    public override async Task<IEnumerable<GachaLogItem>> GetGachaLogAsync(string gachaUrl, IGachaType gachaType, long endId = 0, string? lang = null, IProgress<(IGachaType GachaType, int Page)>? progress = null, CancellationToken cancellationToken = default)
    {
        var auth = ParseRecordUrl(gachaUrl);
        return await GetGachaLogByTypeAsync(auth, gachaType, progress, cancellationToken);
    }


    /// <summary>
    /// 终末地的接口没有 end_id / page / size 这套查询参数，没有对应实现。
    /// </summary>
    public override Task<IEnumerable<GachaLogItem>> GetGachaLogAsync(string gachaUrl, GachaLogQuery query, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Arknights: Endfield does not provide a paged gacha log query API.");
    }


    private async Task<List<GryphlineGachaItem>> GetGachaLogByTypeAsync(GryphlineRecordAuth auth, IGachaType gachaType, IProgress<(IGachaType GachaType, int Page)>? progress, CancellationToken cancellationToken)
    {
        var type = new GryphlineGachaType(gachaType.Value);
        var items = new List<GryphlineGachaItem>();
        string? cursor = null;
        for (int page = 1; page <= MAX_PAGE_PER_POOL; page++)
        {
            progress?.Report((gachaType, page));
            GryphlineGachaPage result = await QueryPageAsync(auth, type, cursor, cancellationToken);
            List<GryphlineGachaRecord> list = result.List ?? [];
            if (list.Count == 0)
            {
                break;
            }
            items.AddRange(ToGachaLogItems(auth, type, list));
            string? next = list[^1].SeqId;
            if (!result.HasMore || string.IsNullOrEmpty(next) || next == cursor)
            {
                break;
            }
            cursor = next;
        }
        return items;
    }


    private async Task<GryphlineGachaPage> QueryPageAsync(GryphlineRecordAuth auth, GryphlineGachaType gachaType, string? cursor, CancellationToken cancellationToken)
    {
        await Task.Delay(Random.Shared.Next(200, 300), cancellationToken);
        var query = HttpUtility.ParseQueryString("");
        query["lang"] = auth.Language;
        query["token"] = auth.Token;
        query["server_id"] = auth.ServerId;
        if (gachaType.ToPoolTypeParameter() is string poolType)
        {
            query["pool_type"] = poolType;
        }
        if (!string.IsNullOrEmpty(cursor))
        {
            query["seq_id"] = cursor;
        }
        string path = gachaType.IsWeapon ? WEAPON_RECORD_PATH : CHARACTER_RECORD_PATH;
        string url = $"https://{auth.Host}{path}?{query}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync(typeof(GryphlineGachaResponse), GryphlineGachaJsonContext.Default, cancellationToken) as GryphlineGachaResponse;
        if (result is null)
        {
            return new GryphlineGachaPage();
        }
        if (result.Code != 0)
        {
            throw new GachaApiException(result.Code, result.Message ?? CoreLang.Gacha_CannotParseTheWishRecordURL);
        }
        return result.Data ?? new GryphlineGachaPage();
    }


    /// <summary>
    /// 把接口记录转成 Starward 的记录。
    /// <para/>
    /// <see cref="GachaLogItem.RankType"/> 存游戏本身的稀有度（终末地最高 6★），
    /// 不折算成米哈游的 5★，统计侧按各游戏的最高稀有度处理。
    /// </summary>
    public static List<GryphlineGachaItem> ToGachaLogItems(GryphlineRecordAuth auth, GryphlineGachaType gachaType, List<GryphlineGachaRecord> records)
    {
        long uid = long.TryParse(auth.ServerId, out long value) ? value : 0;
        var items = new List<GryphlineGachaItem>(records.Count);
        foreach (GryphlineGachaRecord record in records)
        {
            if (string.IsNullOrEmpty(record.SeqId))
            {
                continue;
            }
            bool weapon = gachaType.IsWeapon;
            if (IsEmptyRecord(record, weapon))
            {
                continue;
            }
            items.Add(new GryphlineGachaItem
            {
                Uid = uid,
                Id = ToRecordId(record.SeqId, weapon),
                GachaType = gachaType.Value,
                Name = (weapon ? record.WeaponName : record.CharName) ?? "",
                ItemType = weapon ? "Weapon" : "Character",
                RankType = record.Rarity,
                Time = DateTimeOffset.FromUnixTimeMilliseconds(record.GachaTs).LocalDateTime,
                ItemId = GachaSyntheticId.ToItemId(weapon ? record.WeaponId : record.CharId),
                Count = 1,
                Lang = auth.Language,
                PoolId = record.PoolId,
                IsFree = record.IsFree,
            });
        }
        return items;
    }


    /// <summary>
    /// 有 seqId 却没有物品的记录，不是一抽，是累计 60 抽福利赠送的那张十连券。
    /// <para/>
    /// 十连的每一批记录时间戳完全相同，绝大多数批次都是整整 10 条；偶尔会多出
    /// 第 11 条，charId、charName、rarity 全空。实测两次都出现在「该期卡池累计
    /// 恰好 60 抽之后」的那一批里，而游戏内的寻访记录在同一时刻显示的是一条
    /// 「寻访情报书」——也就是那张券本身。接口描述不了非干员的物品，所以这几个
    /// 字段都是空的。它没有任何可显示的内容，留着只会让卡池总数与「已垫」各多算
    /// 一次。券用掉之后是下一期的前十抽，那十条是正常记录，不受这里影响。
    /// </summary>
    private static bool IsEmptyRecord(GryphlineGachaRecord record, bool weapon)
    {
        return string.IsNullOrEmpty(weapon ? record.WeaponId : record.CharId)
            && string.IsNullOrEmpty(weapon ? record.WeaponName : record.CharName);
    }


    /// <summary>
    /// 角色与武器是两套接口，seqId 各自从头编号，因此把类型编进最低位，
    /// 既保证唯一，也保持按时间递增。
    /// </summary>
    internal static long ToRecordId(string seqId, bool weapon)
    {
        long sequence = long.TryParse(seqId, out long value) ? value : GachaSyntheticId.Fnv1a(seqId) >> 1;
        return sequence * 2 + (weapon ? 1 : 0);
    }


    #endregion



    #region 物品图示


    /// <summary>
    /// 寻访接口只给 charId / weaponId，不给图；官方也没有公开的图鉴接口。
    /// 图示取自社群站 AKEDatabase，它跟着游戏版本解包，数据表与图片都直接公开。
    /// </summary>
    private const string AKEDATA_BASE_URL = "https://data.akedata.wiki";

    private const string AKEDATA_SPRITE_URL = $"{AKEDATA_BASE_URL}/public/images/assets/beyond/dynamicassets/gameplay/ui/sprites";


    /// <summary>
    /// 取所有干员与武器的图示。
    /// <para/>
    /// 数据表约 3MB，先读清单比对版本：<paramref name="knownVersion"/> 与最新版本相同时
    /// 不下载数据表，返回的列表为 null。
    /// <para/>
    /// 干员用头像 <c>charremoteicon/icon_{charId}</c>：数据表里的 iconId 是背包里的物品图，
    /// 不是头像。武器用数据表里的 iconId，它大多与 weaponId 相同，但有少数两把是互换的，
    /// 直接拿 weaponId 拼会拿错图。
    /// <para/>
    /// 记录里存的 ItemId 是 <see cref="GachaSyntheticId.ToItemId"/> 散列后的整数，
    /// 原始字符串没有保存，因此这里对数据表的每个 ID 做同样的散列，反过来对上旧记录。
    /// </summary>
    public async Task<(string Version, List<GachaItemIcon>? Icons)> GetGachaIconsAsync(string? knownVersion, CancellationToken cancellationToken = default)
    {
        string manifestText = await _httpClient.GetStringAsync($"{AKEDATA_BASE_URL}/manifest.json", cancellationToken);
        using JsonDocument manifest = JsonDocument.Parse(manifestText);
        (string version, string tableCfgPath) = GetLatestTableCfg(manifest.RootElement);
        if (version == knownVersion)
        {
            return (version, null);
        }

        await using Stream stream = await _httpClient.GetStreamAsync($"{AKEDATA_BASE_URL}/{tableCfgPath.Trim('/')}/ItemTable.json", cancellationToken);
        using JsonDocument itemTable = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (itemTable.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("AKEDatabase ItemTable is not an object.");
        }
        var icons = new List<GachaItemIcon>();
        foreach (JsonProperty property in itemTable.RootElement.EnumerateObject())
        {
            string key = property.Name;
            string? icon = null;
            if (key.StartsWith("chr_", StringComparison.Ordinal))
            {
                icon = $"{AKEDATA_SPRITE_URL}/charremoteicon/icon_{Uri.EscapeDataString(key)}.png";
            }
            else if (key.StartsWith("wpn_", StringComparison.Ordinal))
            {
                string iconId = GetString(property.Value, "iconId") is { Length: > 0 } id ? id : key;
                icon = $"{AKEDATA_SPRITE_URL}/itemicon/{Uri.EscapeDataString(iconId)}.png";
            }
            if (icon is not null)
            {
                icons.Add(new GachaItemIcon(GachaSyntheticId.ToItemId(key), key, icon));
            }
        }
        if (icons.Count == 0)
        {
            throw new FormatException("AKEDatabase ItemTable has no character or weapon.");
        }
        return (version, icons);
    }


    private static (string Version, string TableCfgPath) GetLatestTableCfg(JsonElement manifest)
    {
        string version = GetString(manifest, "latest");
        if (version.Length > 0 && manifest.TryGetProperty("versions", out JsonElement versions) && versions.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in versions.EnumerateArray())
            {
                if (GetString(item, "id") == version && GetString(item, "tableCfgPath") is { Length: > 0 } path)
                {
                    return (version, path);
                }
            }
        }
        throw new FormatException("AKEDatabase manifest has no table path for the latest version.");
    }


    private static string GetString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }


    #endregion


}
