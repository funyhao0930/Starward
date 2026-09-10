using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Starward.Core.Gacha.Hotta;

/// <summary>
/// 异环「斯卡布罗集市」的抽卡记录客户端。
/// <para/>
/// 与其他游戏最大的不同：<b>异环没有抽卡记录接口</b>。
/// 记录走的是游戏自己的 RPC（客户端二进制里的 <c>LotteryRecord_Req/Rsp</c>、
/// <c>LotteryRecordPageInfo_Req/Rsp</c>），既没有网页版记录页，也就没有可以偷出来的授权 URL；
/// 官方唯一的网页查询在国服完美账号的个人中心，台服与国际服都没有开放。
/// 所以本类不发任何请求，只做两件事：
/// <list type="number">
/// <item>向上层公布卡池列表（<see cref="QueryGachaTypes"/> 是抽卡页面唯一的卡池来源，
/// 即使没有接口也得有个 <see cref="GachaLogClient"/>）；</item>
/// <item>把玩家用 nte-exporter 抓包导出的 JSON 读成 <see cref="HottaGachaItem"/>。</item>
/// </list>
/// 一切与 URL 有关的成员都被改成明确失败或返回空，免得基类那套米哈游流程被误用。
/// </summary>
public class HottaGachaClient : GachaLogClient
{


    /// <summary>
    /// 记录没有服务器端 ID，也没有接口，所有取记录的入口都不该被调用
    /// </summary>
    private const string NoApiMessage = "Neverness to Everness has no gacha record API; import a file exported by nte-exporter instead.";


    public override IReadOnlyCollection<IGachaType> QueryGachaTypes { get; init; } = new HottaGachaType[]
    {
        HottaGachaType.LimitedCharacterBoard,
        HottaGachaType.StandardBoard,
        HottaGachaType.ArcMiracleBox,
        HottaGachaType.MysteryBox,
    }.Cast<IGachaType>().ToList().AsReadOnly();



    public HottaGachaClient(HttpClient? httpClient = null) : base(httpClient)
    {

    }



    #region 没有接口


    /// <summary>
    /// 基类的实现会去游戏目录找 webCaches，对未知的 GameBiz 直接抛异常，
    /// 这里改成返回 null，让抽卡页面走「找不到 URL」的正常提示。
    /// </summary>
    public override string? FindGachaUrlFromLocalFiles(GameBiz gameBiz, string? installPath = null)
    {
        return null;
    }


    /// <summary>
    /// 没有 URL，也就没有能从 URL 里读出来的 uid
    /// </summary>
    public override Task<long> GetUidByGachaUrlAsync(string gachaUrl)
    {
        return Task.FromResult(0L);
    }


    protected override string GetGachaUrlPrefix(string gachaUrl, string? lang = null)
    {
        throw new NotSupportedException(NoApiMessage);
    }


    public override Task<IEnumerable<GachaLogItem>> GetGachaLogAsync(string gachaUrl, long endId = 0, string? lang = null, IProgress<(IGachaType GachaType, int Page)>? progress = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(NoApiMessage);
    }


    public override Task<IEnumerable<GachaLogItem>> GetGachaLogAsync(string gachaUrl, IGachaType gachaType, long endId = 0, string? lang = null, IProgress<(IGachaType GachaType, int Page)>? progress = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(NoApiMessage);
    }


    public override Task<IEnumerable<GachaLogItem>> GetGachaLogAsync(string gachaUrl, GachaLogQuery query, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(NoApiMessage);
    }


    #endregion




    #region 读取 nte-exporter 导出文件


    /// <summary>
    /// 一份导出文件读出来的东西
    /// </summary>
    /// <param name="Uid">文件里的玩家 uid，工具没认出来时是 0</param>
    /// <param name="Items">已经按时间从旧到新排好、合成过 ID 的记录</param>
    /// <param name="DroppedCount">卡池认不出来或时间戳读不了而丢弃的行数，不正常，要让用户知道</param>
    /// <param name="Warnings">抓包过程中的问题，需要原样告诉用户</param>
    /// <remarks>
    /// 导出文件里的每一行都读进来，包括积分与追猎给的奖励。
    /// 斯卡布罗集市一行不等于一抽，但角色几乎都从积分来，在这里滤掉就再也找不回来了；
    /// 谁算一抽由 <c>NteGachaService</c> 按 <see cref="HottaGachaItem.ResultType"/> 判断。
    /// </remarks>
    public record HottaGachaExport(long Uid, List<HottaGachaItem> Items, int DroppedCount, List<string> Warnings);


    /// <summary>
    /// 读入一份 nte-exporter 的抽卡记录导出。
    /// <para/>
    /// 不是这种文件时抛 <see cref="ArgumentException"/>，由上层提示用户。
    /// </summary>
    public static HottaGachaExport ReadExportFile(string path)
    {
        // 该工具以 ensure_ascii=False 写出，一定是 UTF-8，不能交给系统代码页
        return ParseExport(Encoding.UTF8.GetString(ReadFileWithSharing(path)));
    }


    /// <summary>
    /// 解析一份导出文件的内容，文件读取与解析分开是为了能离线测试
    /// </summary>
    public static HottaGachaExport ParseExport(string json)
    {
        HottaExportFile? file;
        try
        {
            file = JsonSerializer.Deserialize(json, HottaGachaJsonContext.Default.HottaExportFile);
        }
        catch (JsonException)
        {
            throw new ArgumentException(CoreLang.Gacha_TheFileIsNotAnNteExporterRecordFile);
        }
        if (file is null)
        {
            throw new ArgumentException(CoreLang.Gacha_TheFileIsNotAnNteExporterRecordFile);
        }
        if (file.Format is HottaExportFile.AchievementFormat)
        {
            // 同一个 exports 目录下的成就导出，结构完全不同
            throw new ArgumentException(CoreLang.Gacha_TheFileIsAnNteExporterAchievementFile);
        }
        if (file.Format != HottaExportFile.HistoryFormat)
        {
            throw new ArgumentException(CoreLang.Gacha_TheFileIsNotAnNteExporterRecordFile);
        }
        // format_version 至今一直是 1，结构却换过好几次，因此不拿它做兼容判断，
        // 更高的版本也照读，读不出来的字段留空即可

        long uid = 0;
        if (!string.IsNullOrWhiteSpace(file.UserUid))
        {
            _ = long.TryParse(file.UserUid, NumberStyles.None, CultureInfo.InvariantCulture, out uid);
        }

        var warnings = new List<string>();
        foreach (HottaExportWarning warning in file.Scan?.Warnings ?? [])
        {
            string text = string.Join(' ', new[] { warning.Code, warning.Reason }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (!string.IsNullOrWhiteSpace(text))
            {
                warnings.Add(text);
            }
        }

        var parsed = new List<(DateTime Time, int Ordinal, string Key, HottaExportRecord Record, int GachaType)>();
        int dropped = 0;
        foreach (HottaExportRecord record in file.Records ?? [])
        {
            int gachaType = HottaGachaType.FromPoolId(record.PoolGroupId);
            if (gachaType == 0 || !TryParseWallClockTime(record.Timestamp, out DateTime time))
            {
                // 认不出来的卡池不能悄悄并进别的池子，读不了的时间戳也没法排序，只能丢
                dropped++;
                continue;
            }
            parsed.Add((time, record.TimestampGroupOrdinal ?? 0, record.Uid ?? "", record, gachaType));
        }

        // 文件里是从新到旧排的，但第三方工具的顺序不保证，这里显式排成从旧到新：
        // 同一时间戳内 ordinal 0 是最新的，所以要倒过来。排序只决定返回的顺序，
        // 不参与 ID 的计算，因此拿工具那个不稳定的记录 ID 兜底也无妨。
        var ordered = parsed.OrderBy(x => x.Time)
                            .ThenByDescending(x => x.Ordinal)
                            .ThenBy(x => x.Key, StringComparer.Ordinal)
                            .ToList();

        // 正常情况下同一秒内的 ordinal 互不相同，直接拿它算序号，抓包深浅变化也不会改 ID。
        // 但同一显示秒里可能落进两组原始时间戳，整份文件也可能根本没写 ordinal（全当 0），
        // 这时同一秒会出现重复的 ordinal，两条记录会算出同一个 ID，
        // 其中一条就被 INSERT OR REPLACE 悄悄覆盖掉。这种文件退回按位置数：
        // ID 会随抓包深浅变化，但至少不会丢记录。
        bool ordinalsAreUnique = ordered.GroupBy(x => (x.Time, x.Ordinal)).All(x => x.Count() is 1);

        var items = new List<HottaGachaItem>(ordered.Count);
        var positions = new Dictionary<DateTime, int>();
        foreach ((DateTime time, int ordinal, _, HottaExportRecord record, int gachaType) in ordered)
        {
            positions.TryGetValue(time, out int position);
            positions[time] = position + 1;
            // ordered 在同一秒内已经是从旧到新，按位置数出来的序号也就与时间同向
            int sequence = ordinalsAreUnique ? ToSequence(ordinal) : Math.Clamp(position, 0, MaxSequence);
            string itemId = record.RewardId ?? "";
            items.Add(new HottaGachaItem
            {
                Uid = uid,
                // ID 必须在多次导出之间保持不变，否则重复导入会插入重复记录，
                // 因此只用记录自身固有的东西：时间、组内序号、卡池与物品 ID。
                // 标成 UTC 是为了让 FromTime 里的换算变成空操作，换时区不会改 ID；
                // 名称来自该工具会更新的对照表，不能进 ID。
                Id = GachaSyntheticId.FromTime(DateTime.SpecifyKind(time, DateTimeKind.Utc), sequence, $"{gachaType}|{itemId}"),
                GachaType = gachaType,
                Name = NormalizeName(record.RewardName, itemId),
                ItemType = record.RewardType ?? "",
                ResultType = record.ResultType,
                RewardId = itemId,
                RankType = ToRankType(record.RewardRank),
                Time = time,
                ItemId = GachaSyntheticId.ToItemId(itemId),
                Count = record.Quantity is > 0 ? record.Quantity.Value : 1,
                Lang = "en",
            });
        }
        return new HottaGachaExport(uid, items, dropped, warnings);
    }


    /// <summary>
    /// 字母稀有度换成 Starward 通用的星数档位，认不出来给 0（界面按未知处理）
    /// </summary>
    public static int ToRankType(string? rank) => rank?.Trim().ToUpperInvariant() switch
    {
        "S" => 5,
        "A" => 4,
        "B" => 3,
        _ => 0,
    };


    /// <summary>
    /// 名字对不上时工具会写空串或字面量 UNKNOWN，两种都退回物品 ID，
    /// 免得界面上出现一排「UNKNOWN」
    /// </summary>
    private static string NormalizeName(string? name, string itemId)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "UNKNOWN")
        {
            return itemId;
        }
        return name;
    }


    /// <summary>
    /// 组内序号换成 <see cref="GachaSyntheticId.FromTime"/> 要的「同一秒内按时间升序的序号」。
    /// <para/>
    /// 该工具的 ordinal 0 是同一组里最新的一条，而且是它明确保证稳定的东西：
    /// 抓得更深只会往后追加更大的 ordinal，已有记录的 ordinal 不变。
    /// 因此直接倒过来用，比按文件里的位置数更可靠——后者会随抓包深浅变化，
    /// 同一抽在两份导出里就会算出两个 ID。
    /// <para/>
    /// 只在同一秒内的 ordinal 确实互不相同时才这么算，撞号的文件另走位置数，
    /// 判断见 <c>ordinalsAreUnique</c>。
    /// </summary>
    private static int ToSequence(int ordinal)
    {
        return Math.Clamp(MaxSequence - ordinal, 0, MaxSequence);
    }


    /// <summary>
    /// <see cref="GachaSyntheticId.FromTime"/> 每秒只留 100 个序号
    /// </summary>
    private const int MaxSequence = 99;


    /// <summary>
    /// 读时间戳。
    /// <para/>
    /// 该工具用 <c>datetime.fromtimestamp(..., timezone.utc)</c> 渲染，看起来像 UTC，
    /// 但游戏发过来的刻度本来就是服务器当地时间，所以渲染出来的字符串
    /// <b>就是游戏里显示的那个时间</b>，不能再当成 UTC 换一次时区。
    /// 实测：游戏内记录写 2026/9/1 21:33:48，当成 UTC 转本地会变成 9/2 05:33:48，正好差了 8 小时。
    /// <para/>
    /// 因此原样读成挂钟时间存库；算 ID 时把它标成 UTC 只是为了让
    /// <see cref="GachaSyntheticId.FromTime"/> 里的 <c>ToUniversalTime()</c> 不做任何换算，
    /// 这样换时区也不会算出不同的 ID。
    /// </summary>
    private static bool TryParseWallClockTime(string? text, out DateTime time)
    {
        if (DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value)
            || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            time = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
            return true;
        }
        time = default;
        return false;
    }


    #endregion


}
