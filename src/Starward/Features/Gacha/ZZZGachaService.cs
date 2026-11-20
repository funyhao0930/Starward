using Dapper;
using Microsoft.Extensions.Logging;
using MiniExcelLibs;
using Starward.Core;
using Starward.Core.Gacha;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.StarRail;
using Starward.Core.Gacha.ZZZ;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.ZZZ.GachaRecord;
using Starward.Features.Database;
using Starward.Features.Gacha.UIGF;
using Starward.Features.GameRecord;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Gacha;

internal class ZZZGachaService : GachaLogService
{


    protected override GameBiz CurrentGameBiz { get; } = GameBiz.nap;

    protected override string GachaTableName { get; } = "ZZZGachaItem";


    private readonly GameRecordService _gameRecordService;



    public ZZZGachaService(ILogger<ZZZGachaService> logger, ZZZGachaClient client, GameRecordService gameRecordService) : base(logger, client)
    {
        _gameRecordService = gameRecordService;
    }


    protected override List<GachaLogItemEx> GetGachaLogItemsByQueryType(IEnumerable<GachaLogItemEx> items, IGachaType type)
    {
        return type switch
        {
            _ => items.Where(x => x.GachaType == type.Value).ToList(),
        };
    }



    public override List<GachaLogItemEx> GetGachaLogItemEx(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<GachaLogItemEx>("""
            SELECT item.*, info.Icon FROM ZZZGachaItem item LEFT JOIN ZZZGachaInfo info ON item.ItemId=Info.Id WHERE Uid=@uid ORDER BY item.Id;
            """, new { uid }).ToList();
        foreach (var type in QueryGachaTypes)
        {
            var l = GetGachaLogItemsByQueryType(list, type);
            int index = 0;
            int pity = 0;
            bool hasNoUp = GachaNoUp.TryGet(CurrentGameBiz, type.Value, out GachaNoUp? noUp);
            foreach (var item in l)
            {
                item.Index = ++index;
                item.Pity = ++pity;
                if (item.RankType == 4)
                {
                    pity = 0;
                    item.HasUpItem = hasNoUp;
                    if (hasNoUp)
                    {
                        item.IsUp = noUp!.IsUp(item);
                    }
                }
            }
        }
        return list;
    }



    public override async Task<long> GetGachaLogAsync(string url, bool all, string? lang = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        using var dapper = DatabaseService.CreateConnection();
        // 正在获取 uid
        progress?.Report(Lang.GachaLogService_GettingUid);
        var uid = await _client.GetUidByGachaUrlAsync(url);
        if (uid == 0)
        {
            // 该账号最近6个月没有抽卡记录
            progress?.Report(Lang.GachaLogService_ThisAccountHasNoGachaRecordsInTheLast6Months);
        }
        else
        {
            long endId = 0;
            if (!all)
            {
                endId = dapper.QueryFirstOrDefault<long>($"SELECT Id FROM {GachaTableName} WHERE Uid = @Uid ORDER BY Id DESC LIMIT 1;", new { Uid = uid });
                _logger.LogInformation($"Last gacha log id of uid {uid} is {endId}");
            }

            var internalProgress = new Progress<(IGachaType GachaType, int Page)>((x) => progress?.Report(string.Format(Lang.GachaLogService_GetGachaProgressText, x.GachaType.ToLocalization(), x.Page)));
            var list = (await _client.GetGachaLogAsync(url, endId, lang, internalProgress, cancellationToken)).ToList();
            if (cancellationToken.IsCancellationRequested)
            {
                throw new TaskCanceledException();
            }
            var oldCount = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {GachaTableName} WHERE Uid = @Uid;", new { Uid = uid });
            InsertGachaLogItems(list);
            var newCount = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {GachaTableName} WHERE Uid = @Uid;", new { Uid = uid });
            // 获取 {list.Count} 条记录，新增 {newCount - oldCount} 条记录
            progress?.Report(string.Format(Lang.GachaLogService_GetGachaResult, list.Count, newCount - oldCount));
        }
        return uid;
    }


    public async Task<long> GetGachaLogByGameRecordAsync(GameRecordRole role, bool all, string? lang = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (role is null)
        {
            throw new ArgumentNullException(nameof(role));
        }
        using var dapper = DatabaseService.CreateConnection();
        // 正在获取 uid
        progress?.Report(Lang.GachaLogService_GettingUid);
        long uid = role.Uid;
        if (uid == 0)
        {
            // 该账号最近6个月没有抽卡记录
            progress?.Report(Lang.GachaLogService_ThisAccountHasNoGachaRecordsInTheLast6Months);
            return 0;
        }

        // requestLanguage: global 场景下，用于更新 HoYoLAB 请求头语言。
        // recordLanguage: 用于本地落库语言标记与 item_type 映射规则（国服固定 zh-cn）。
        bool isGlobal = role.GameBiz?.EndsWith("_global", StringComparison.OrdinalIgnoreCase) ?? false;
        string recordLanguage = "zh-cn";
        string? requestLanguage = null;
        if (isGlobal)
        {
            string sourceLanguage = string.IsNullOrWhiteSpace(lang)
                ? System.Globalization.CultureInfo.CurrentUICulture.Name
                : lang;
            string normalizedLanguage = LanguageUtil.FilterLanguage(sourceLanguage);

            requestLanguage = normalizedLanguage;
            recordLanguage = normalizedLanguage;
        }

        long endId = 0;
        if (!all)
        {
            endId = dapper.QueryFirstOrDefault<long>($"SELECT Id FROM {GachaTableName} WHERE Uid = @Uid ORDER BY Id DESC LIMIT 1;", new { Uid = uid });
            _logger.LogInformation("Last gacha log id of uid {Uid} is {EndId}", uid, endId);
        }

        var list = new List<GachaLogItem>();
        foreach (IGachaType queryType in QueryGachaTypes)
        {
            ZZZGachaType gachaType = queryType.Value;
            bool stop = false;
            long? queryEndId = null;
            int page = 1;
            while (!stop)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(string.Format(Lang.GachaLogService_GetGachaProgressText, queryType.ToLocalization(), page));
                var data = await _gameRecordService.GetZZZGachaRecordAsync(role, gachaType, queryEndId, requestLanguage, cancellationToken);
                var pageItems = data.GachaItemList ?? new List<ZZZGachaRecordItem>();
                if (pageItems.Count == 0)
                {
                    break;
                }
                foreach (var item in pageItems)
                {
                    if (!all && endId > 0 && item.Id <= endId)
                    {
                        stop = true;
                        break;
                    }
                    list.Add(ToGachaLogItem(uid, gachaType, item, recordLanguage));
                }
                if (stop || !data.HasMore)
                {
                    break;
                }
                long nextEndId = pageItems.Last().Id;
                if (nextEndId <= 0 || nextEndId == queryEndId.GetValueOrDefault())
                {
                    _logger.LogWarning("ZZZ gacha_record paging is not advancing (Uid: {Uid}, GachaType: {GachaType}, EndId: {EndId}). Stop paging to avoid infinite loop.", uid, gachaType, nextEndId);
                    break;
                }
                queryEndId = nextEndId;
                page++;
            }
        }

        var oldCount = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {GachaTableName} WHERE Uid = @Uid;", new { Uid = uid });
        InsertGachaLogItems(list);
        var newCount = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {GachaTableName} WHERE Uid = @Uid;", new { Uid = uid });
        // 获取 {list.Count} 条记录，新增 {newCount - oldCount} 条记录
        progress?.Report(string.Format(Lang.GachaLogService_GetGachaResult, list.Count, newCount - oldCount));
        return uid;
    }


    #region HoYoLAB 调频统计工具


    // 调频统计工具只给 S 级出货（自开服起）和各频段总抽数，没有 A/B 级逐抽记录，所以不写进 ZZZGachaItem：
    // 逐抽表的保底、概率、平均都是按行数算的，混进去会全部算错。这里另存一张表，统计时再用 Sort（累计抽数）
    // 把本地记录对齐到全历史，补上本地记录开始之前的 S 级与抽数。


    private sealed class OfficialGachaS
    {
        public long Uid { get; set; }
        public int GachaType { get; set; }
        public int Sort { get; set; }
        public int ItemId { get; set; }
        public string Name { get; set; } = "";
        public string ItemType { get; set; } = "";
        public string Icon { get; set; } = "";
        public bool IsUp { get; set; }
        public int Count { get; set; }
        public DateTime Time { get; set; }
    }


    private sealed class OfficialGachaData
    {
        public int TotalCount { get; set; }
        public List<OfficialGachaS> Records { get; set; } = new();
    }


    /// <summary>
    /// 调频统计工具 hist_pool_stat 一次最多能带几个版本，超过会回 -400005
    /// </summary>
    private const int OfficialGachaVersionsPerRequest = 8;


    public async Task<long> SyncOfficialGachaStatsAsync(GameRecordRole role, string? lang = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        long uid = role.Uid;
        progress?.Report(Lang.GachaLogService_GettingUid);
        string language = LanguageUtil.FilterLanguage(string.IsNullOrWhiteSpace(lang) ? System.Globalization.CultureInfo.CurrentUICulture.Name : lang);
        bool isChinese = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        TimeSpan serverOffset = GetServerTimeOffset(role.Region);

        var index = await _gameRecordService.GetZZZOfficialGachaIndexAsync(role, language, cancellationToken);
        var records = new List<OfficialGachaS>();
        var stats = new List<object>();
        DateTime now = DateTime.Now;
        foreach (var typeStat in index.TypeStats)
        {
            int gachaType = typeStat.GachaTypeValue;
            if (gachaType == 0)
            {
                _logger.LogWarning("Unknown ZZZ official gacha type {GachaType}", typeStat.GachaType);
                continue;
            }
            stats.Add(new { Uid = uid, GachaType = gachaType, typeStat.TotalCount, UpdateTime = now });
            if (typeStat.SCount == 0)
            {
                continue;
            }
            string typeName = new ZZZGachaType(gachaType).ToLocalization();
            int page = 1;
            progress?.Report(string.Format(Lang.GachaLogService_GetGachaProgressText, typeName, page));
            var pools = new List<ZZZOfficialGachaPool>();
            pools.AddRange((await _gameRecordService.GetZZZOfficialGachaPoolsAsync(role, gachaType, null, language, cancellationToken)).List);
            // 当前版本由 cur_pool_stat 给，hist_pool_stat 只查以前的版本
            var versions = typeStat.HasDataVersions.Where(x => x != index.CurrentVersion).ToList();
            foreach (var chunk in versions.Chunk(OfficialGachaVersionsPerRequest))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(string.Format(Lang.GachaLogService_GetGachaProgressText, typeName, ++page));
                pools.AddRange((await _gameRecordService.GetZZZOfficialGachaPoolsAsync(role, gachaType, chunk, language, cancellationToken)).List);
            }
            // 同一只 S 会同时出现在当前与历史卡池里；十连里两只相同的 S 物品与时间都一样，所以用 Sort 去重
            var typeRecords = pools.SelectMany(x => x.Records)
                                   .Where(x => string.Equals(x.Rarity, "S", StringComparison.OrdinalIgnoreCase) && x.Sort > 0)
                                   .GroupBy(x => x.Sort)
                                   .Select(x => x.First())
                                   .OrderBy(x => x.Sort)
                                   .Select(x => new OfficialGachaS
                                   {
                                       Uid = uid,
                                       GachaType = gachaType,
                                       Sort = x.Sort,
                                       ItemId = x.ItemId,
                                       Name = x.Name,
                                       ItemType = ToLocalItemType(x.ItemType, isChinese),
                                       Icon = x.Icon,
                                       IsUp = x.IsUp,
                                       Count = x.Count,
                                       Time = DateTimeOffset.FromUnixTimeSeconds(x.RecordTime).ToOffset(serverOffset).DateTime,
                                   })
                                   .ToList();
            if (typeRecords.Count != typeStat.SCount)
            {
                _logger.LogWarning("ZZZ official gacha type {GachaType}: got {Count} S records, index says {SCount}", gachaType, typeRecords.Count, typeStat.SCount);
            }
            records.AddRange(typeRecords);
        }

        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        dapper.Execute("DELETE FROM ZZZOfficialGachaS WHERE Uid = @uid;", new { uid }, t);
        dapper.Execute("""
            INSERT OR REPLACE INTO ZZZOfficialGachaS (Uid, GachaType, Sort, ItemId, Name, ItemType, Icon, IsUp, Count, Time)
            VALUES (@Uid, @GachaType, @Sort, @ItemId, @Name, @ItemType, @Icon, @IsUp, @Count, @Time);
            """, records, t);
        dapper.Execute("DELETE FROM ZZZOfficialGachaStats WHERE Uid = @uid;", new { uid }, t);
        dapper.Execute("""
            INSERT OR REPLACE INTO ZZZOfficialGachaStats (Uid, GachaType, TotalCount, UpdateTime)
            VALUES (@Uid, @GachaType, @TotalCount, @UpdateTime);
            """, stats, t);
        t.Commit();
        progress?.Report(string.Format(Lang.ZZZGachaService_OfficialGachaStatsResult, records.Count));
        return uid;
    }


    public override List<long> GetUids()
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<long>($"SELECT Uid FROM {GachaTableName} UNION SELECT Uid FROM ZZZOfficialGachaStats;").ToList();
    }


    private static TimeSpan GetServerTimeOffset(string? region) => region switch
    {
        "prod_gf_us" => TimeSpan.FromHours(-5),
        "prod_gf_eu" => TimeSpan.FromHours(1),
        _ => TimeSpan.FromHours(8),
    };


    private static string ToLocalItemType(string itemType, bool isChinese)
    {
        if (!isChinese)
        {
            return itemType;
        }
        return itemType.ToUpperInvariant() switch
        {
            "ITEMTYPE_AVATAR" or "ITEM_TYPE_AVATAR" => "代理人",
            "ITEMTYPE_WEAPON" or "ITEM_TYPE_WEAPON" => "音擎",
            "ITEMTYPE_BANGBOO" or "ITEM_TYPE_BANGBOO" => "邦布",
            _ => itemType,
        };
    }


    private Dictionary<int, OfficialGachaData> LoadOfficialGachaData(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        var result = dapper.Query<(int GachaType, int TotalCount)>("SELECT GachaType, TotalCount FROM ZZZOfficialGachaStats WHERE Uid = @uid AND TotalCount > 0;", new { uid })
                           .ToDictionary(x => x.GachaType, x => new OfficialGachaData { TotalCount = x.TotalCount });
        if (result.Count == 0)
        {
            return result;
        }
        var icons = dapper.Query<(int Id, string Icon)>("SELECT Id, Icon FROM ZZZGachaInfo;").GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First().Icon);
        foreach (var record in dapper.Query<OfficialGachaS>("SELECT * FROM ZZZOfficialGachaS WHERE Uid = @uid ORDER BY GachaType, Sort;", new { uid }))
        {
            if (icons.TryGetValue(record.ItemId, out string? icon) && !string.IsNullOrWhiteSpace(icon))
            {
                record.Icon = icon;
            }
            if (result.TryGetValue(record.GachaType, out var data))
            {
                data.Records.Add(record);
            }
        }
        return result;
    }


    /// <summary>
    /// 找出本地第 1 抽对应官方累计第几抽之前有几抽（offset）。本地记录是全历史的一段连续区间，
    /// 落在官方范围内的每只本地 S 都要对上同 Sort、同物品的官方 S，官方在这段区间内的 S 也都要在本地出现。
    /// </summary>
    private static int? FindOfficialOffset(List<GachaLogItemEx> local, OfficialGachaData official)
    {
        if (local.Count == 0)
        {
            // 没有本地记录，整段都用官方的
            return official.TotalCount;
        }
        var localS = new List<(int Index, GachaLogItemEx Item)>();
        for (int i = 0; i < local.Count; i++)
        {
            if (local[i].RankType == 4)
            {
                localS.Add((i + 1, local[i]));
            }
        }
        var bySort = official.Records.ToDictionary(x => x.Sort);
        int? best = null;
        double bestScore = double.MaxValue;
        foreach (int offset in localS.SelectMany(s => official.Records.Where(o => o.ItemId == s.Item.ItemId).Select(o => o.Sort - s.Index)).Where(x => x >= 0).Distinct())
        {
            bool ok = true;
            double timeDiff = 0;
            foreach (var (index, item) in localS)
            {
                int sort = offset + index;
                if (sort > official.TotalCount)
                {
                    // 同步官方统计之后才抽到的
                    continue;
                }
                if (!bySort.TryGetValue(sort, out var o) || o.ItemId != item.ItemId)
                {
                    ok = false;
                    break;
                }
                timeDiff += Math.Abs((o.Time - item.Time).TotalMinutes);
            }
            if (ok)
            {
                int end = offset + local.Count;
                ok = official.Records.Where(o => o.Sort > offset && o.Sort <= end).All(o => local[o.Sort - offset - 1].RankType == 4);
            }
            if (ok && timeDiff < bestScore)
            {
                best = offset;
                bestScore = timeDiff;
            }
        }
        return best;
    }


    private static bool TryBuildMergedStats(IGachaType type, List<GachaLogItemEx> local, OfficialGachaData official, out GachaTypeStats stats, out List<GachaLogItemEx> preItems)
    {
        stats = null!;
        preItems = new List<GachaLogItemEx>();
        if (FindOfficialOffset(local, official) is not int offset)
        {
            return false;
        }
        bool hasUpItem = GachaNoUp.TryGet(GameBiz.nap, type.Value, out _);
        var bySort = official.Records.ToDictionary(x => x.Sort);

        // 按全历史抽数排好的 S：本地记录开始之前、以及本地最后一抽之后（本地还没更新）的用官方的，
        // 中间用本地的（物品对得上时 UP 以官方为准）
        int localEnd = offset + local.Count;
        var sList = new List<(int Global, GachaLogItemEx Item)>();
        var officialItems = new List<GachaLogItemEx>();
        preItems = officialItems;
        GachaLogItemEx ToOfficialItem(OfficialGachaS o)
        {
            var item = new GachaLogItemEx
            {
                Uid = o.Uid,
                GachaType = o.GachaType,
                Name = o.Name,
                ItemType = o.ItemType,
                ItemId = o.ItemId,
                RankType = 4,
                Time = o.Time,
                Count = 1,
                Icon = o.Icon,
                Index = o.Sort,
                IsUp = o.IsUp,
                HasUpItem = hasUpItem,
            };
            officialItems.Add(item);
            return item;
        }
        foreach (var o in official.Records.Where(x => x.Sort <= offset))
        {
            sList.Add((o.Sort, ToOfficialItem(o)));
        }
        for (int i = 0; i < local.Count; i++)
        {
            var item = local[i];
            if (item.RankType == 4)
            {
                if (bySort.TryGetValue(offset + i + 1, out var o) && o.ItemId == item.ItemId)
                {
                    item.IsUp = o.IsUp;
                }
                sList.Add((offset + i + 1, item));
            }
        }
        foreach (var o in official.Records.Where(x => x.Sort > localEnd))
        {
            sList.Add((o.Sort, ToOfficialItem(o)));
        }
        int last = 0;
        foreach (var (global, item) in sList)
        {
            item.Pity = global - last;
            last = global;
        }

        int count = Math.Max(localEnd, official.TotalCount);
        stats = new GachaTypeStats
        {
            GachaType = type.Value,
            GachaTypeText = type.ToLocalization(),
            Count = count,
            Count_5 = sList.Count,
            Count_5_Up = sList.Count(x => x.Item.IsUp),
            Count_4 = local.Count(x => x.RankType == 3),
            Count_3 = local.Count(x => x.RankType == 2),
            StartTime = sList.Select(x => x.Item.Time).Concat(local.Select(x => x.Time)).DefaultIfEmpty().Min(),
            EndTime = sList.Select(x => x.Item.Time).Concat(local.Select(x => x.Time)).DefaultIfEmpty().Max(),
        };
        stats.Ratio_5 = count == 0 ? 0 : (double)stats.Count_5 / count;
        // 官方没有 A/B 级，只能按本地记录算
        stats.Ratio_4 = local.Count == 0 ? 0 : (double)stats.Count_4 / local.Count;
        stats.Ratio_3 = local.Count == 0 ? 0 : (double)stats.Count_3 / local.Count;
        stats.Pity_5 = count - last;
        stats.Average_5 = stats.Count_5 == 0 ? 0 : (double)(count - stats.Pity_5) / stats.Count_5;
        if (stats.Count_5_Up > 0)
        {
            stats.Average_5_Up = (double)(count - stats.Pity_5) / stats.Count_5_Up;
        }
        stats.Pity_4 = local.Count - 1 - local.FindLastIndex(x => x.RankType == 3);
        int pity_4 = 0;
        foreach (var item in local)
        {
            pity_4++;
            if (item.RankType == 3)
            {
                item.Pity = pity_4;
                pity_4 = 0;
            }
        }
        stats.List_5 = sList.Select(x => x.Item).Reverse().ToList();
        stats.List_4 = local.Where(x => x.RankType == 3).Reverse().ToList();
        DateTime lastTime = stats.EndTime;
        stats.List_5.Insert(0, new GachaLogItemEx
        {
            Name = Lang.GachaStatsCard_Pity,
            Pity = stats.Pity_5,
            Time = lastTime,
            HasUpItem = hasUpItem,
        });
        stats.List_4.Insert(0, new GachaLogItemEx
        {
            Name = Lang.GachaStatsCard_Pity,
            Pity = stats.Pity_4,
            Time = lastTime,
            HasUpItem = hasUpItem,
        });
        return true;
    }


    #endregion



    public override (List<GachaTypeStats> GachaStats, List<GachaLogItemEx> ItemStats) GetGachaTypeStats(long uid)
    {
        var statsList = new List<GachaTypeStats>();
        var groupStats = new List<GachaLogItemEx>();
        using var dapper = DatabaseService.CreateConnection();
        var allItems = GetGachaLogItemEx(uid);
        var official = LoadOfficialGachaData(uid);
        if (allItems.Count > 0 || official.Count > 0)
        {
            var officialOnlyItems = new List<GachaLogItemEx>();
            foreach (IGachaType type in QueryGachaTypes)
            {
                var list = GetGachaLogItemsByQueryType(allItems, type);
                if (official.TryGetValue(type.Value, out var officialData)
                    && TryBuildMergedStats(type, list, officialData, out var mergedStats, out var preItems))
                {
                    statsList.Add(mergedStats);
                    officialOnlyItems.AddRange(preItems);
                    continue;
                }
                if (list.Count == 0)
                {
                    continue;
                }
                var stats = new GachaTypeStats
                {
                    GachaType = type.Value,
                    GachaTypeText = type.ToLocalization(),
                    Count = list.Count,
                    Count_5_Up = list.Count(x => x.RankType == 4 && x.IsUp),
                    Count_5 = list.Count(x => x.RankType == 4),
                    Count_4 = list.Count(x => x.RankType == 3),
                    Count_3 = list.Count(x => x.RankType == 2),
                    StartTime = list.First().Time,
                    EndTime = list.Last().Time
                };
                stats.Ratio_5 = (double)stats.Count_5 / stats.Count;
                stats.Ratio_4 = (double)stats.Count_4 / stats.Count;
                stats.Ratio_3 = (double)stats.Count_3 / stats.Count;
                stats.List_5 = list.Where(x => x.RankType == 4).Reverse().ToList();
                stats.List_4 = list.Where(x => x.RankType == 3).Reverse().ToList();
                stats.Pity_5 = list.Last().Pity;
                if (list.Last().RankType == 4)
                {
                    stats.Pity_5 = 0;
                }
                stats.Average_5 = (double)(stats.Count - stats.Pity_5) / stats.Count_5;
                stats.Pity_4 = list.Count - 1 - list.FindLastIndex(x => x.RankType == 3);

                if (stats.Count_5_Up > 0)
                {
                    int c = stats.Count - stats.Pity_5;
                    stats.Average_5_Up = (double)c / stats.Count_5_Up;
                }

                int pity_4 = 0;
                foreach (var item in list)
                {
                    pity_4++;
                    if (item.RankType == 3)
                    {
                        item.Pity = pity_4;
                        pity_4 = 0;
                    }
                }

                statsList.Add(stats);
                if (CurrentGameBiz == GameBiz.hk4e && type.Value == GenshinGachaType.NoviceWish && stats.Count == 20)
                {
                    continue;
                }
                else if (CurrentGameBiz == GameBiz.hkrpg && type.Value == StarRailGachaType.DepartureWarp && stats.Count == 50)
                {
                    continue;
                }
                else
                {
                    stats.List_5.Insert(0, new GachaLogItemEx
                    {
                        Name = Lang.GachaStatsCard_Pity,
                        Pity = stats.Pity_5,
                        Time = list.Last().Time,
                        HasUpItem = GachaNoUp.TryGet(CurrentGameBiz, type.Value, out _),
                    });
                    stats.List_4.Insert(0, new GachaLogItemEx
                    {
                        Name = Lang.GachaStatsCard_Pity,
                        Pity = stats.Pity_4,
                        Time = list.Last().Time,
                        HasUpItem = GachaNoUp.TryGet(CurrentGameBiz, type.Value, out _),
                    });
                }
            }
            groupStats = allItems.Concat(officialOnlyItems).GroupBy(x => x.ItemId)
                                 .Select(x => { var item = x.First(); item.ItemCount = x.Count(); return item; })
                                 .OrderByDescending(x => x.RankType)
                                 .ThenByDescending(x => x.ItemCount)
                                 .ThenByDescending(x => x.Time)
                                 .ToList();
        }
        return (statsList, groupStats);
    }




    protected override int InsertGachaLogItems(List<GachaLogItem> items)
    {
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        var affect = dapper.Execute("""
            INSERT OR REPLACE INTO ZZZGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, Count, Lang)
            VALUES (@Uid, @Id, @Name, @Time, @ItemId, @ItemType, @RankType, @GachaType, @Count, @Lang);
            """, items, t);
        t.Commit();
        return affect;
    }



    public override async Task ExportGachaLogAsync(long uid, string file, string format)
    {
        if (format is "excel")
        {
            await ExportAsExcelAsync(uid, file);
        }
        else
        {
            await ExportAsJsonAsync(uid, file);
        }
    }



    private async Task ExportAsJsonAsync(long uid, string output)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<ZZZGachaItem>($"SELECT * FROM {GachaTableName} WHERE Uid = @uid ORDER BY Id;", new { uid }).ToList();
        DateTimeOffset time = DateTimeOffset.Now;
        var uigfObj = new UIGF3File<ZZZGachaItem>
        {
            Info = new UIAF3FileInfo
            {
                Uid = uid,
                Lang = list.Last().Lang ?? "",
                ExportTimestamp = time.ToUnixTimeSeconds(),
                ExportTime = time.ToString("yyyy-MM-dd HH:mm:ss"),
                ExportAppVersion = AppConfig.AppVersion,
            },
            List = list,
        };
        using FileStream fs = File.Create(output);
        await JsonSerializer.SerializeAsync(fs, uigfObj, AppConfig.JsonSerializerOptions);
    }


    private async Task ExportAsExcelAsync(long uid, string output)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = GetGachaLogItemEx(uid);
        var template = Path.Combine(AppContext.BaseDirectory, @"Assets\Template\ZZZGachaLog.xlsx");
        if (File.Exists(template))
        {
            await MiniExcel.SaveAsByTemplateAsync(output, template, new { list });
        }
    }




    public override long ImportGachaLog(string file)
    {
        var str = File.ReadAllText(file);
        var obj = JsonSerializer.Deserialize<UIGF3File<ZZZGachaItem>>(str);
        if (obj != null)
        {
            string lang = obj.Info.Lang ?? "";
            long uid = obj.Info.Uid;
            foreach (var item in obj.List)
            {
                if (item.Lang is null)
                {
                    item.Lang = lang;
                }
                if (item.Uid == 0)
                {
                    item.Uid = uid;
                }
            }
            var count = InsertGachaLogItems(obj.List.ToList<GachaLogItem>());
            // 成功导入调频记录 {count} 条
            InAppToast.MainWindow?.Success($"Uid {obj.Info.Uid}", string.Format(Lang.ZZZGachaService_ImportSignalSearchRecordsSuccessfully, count), 5000);
            return obj.Info.Uid;
        }
        return 0;
    }



    public override async Task<string> UpdateGachaInfoAsync(GameBiz gameBiz, string lang, CancellationToken cancellationToken = default)
    {
        var data = await _client.GetZZZGachaInfoAsync(gameBiz, lang, cancellationToken);
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        const string insertSql = """
            INSERT OR REPLACE INTO ZZZGachaInfo (Id, Name, Icon, Rarity, ElementType, Profession)
            VALUES (@Id, @Name, @Icon, @Rarity, @ElementType, @Profession);
            """;
        dapper.Execute(insertSql, data.List, t);
        t.Commit();
        return data.Language;
    }


    public override async Task<(string Language, int Count)> ChangeGachaItemNameAsync(GameBiz gameBiz, string lang, CancellationToken cancellationToken = default)
    {
        lang = await UpdateGachaInfoAsync(gameBiz, lang, cancellationToken);
        using var dapper = DatabaseService.CreateConnection();
        int count = dapper.Execute("""
             INSERT OR REPLACE INTO ZZZGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, Count, Lang)
             SELECT item.Uid, item.Id, info.Name, Time, ItemId, ItemType, RankType, GachaType, Count, @Lang
             FROM ZZZGachaItem item INNER JOIN ZZZGachaInfo info ON item.ItemId = info.Id;
             """, new { Lang = lang });
        return (lang, count);
    }



    private static GachaLogItem ToGachaLogItem(long uid, ZZZGachaType gachaType, ZZZGachaRecordItem item, string language)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (uid == 0)
        {
            throw new ArgumentException("Uid cannot be 0.", nameof(uid));
        }
        if (gachaType.Value == 0)
        {
            throw new ArgumentException("GachaType cannot be 0.", nameof(gachaType));
        }
        if (item.Id == 0)
        {
            throw new ArgumentException("Id cannot be 0.", nameof(item));
        }
        if (item.ItemId == 0)
        {
            throw new ArgumentException("ItemId cannot be 0.", nameof(item));
        }
        if (item.Date == default)
        {
            throw new ArgumentException("Date cannot be default value.", nameof(item));
        }
        if (string.IsNullOrWhiteSpace(item.ItemName))
        {
            throw new ArgumentException("ItemName cannot be null or empty.", nameof(item));
        }
        if (string.IsNullOrWhiteSpace(item.ItemType))
        {
            throw new ArgumentException("ItemType cannot be null or empty.", nameof(item));
        }
        if (string.IsNullOrWhiteSpace(item.Rarity))
        {
            throw new ArgumentException("Rarity cannot be null or empty.", nameof(item));
        }
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("Language cannot be null or empty.", nameof(language));
        }

        int rankType = item.Rarity.ToUpperInvariant() switch
        {
            "S" => 4,
            "A" => 3,
            "B" => 2,
            _ => 0,
        };
        if (rankType == 0)
        {
            throw new ArgumentException($"Invalid rarity value: {item.Rarity}.", nameof(item));
        }

        bool isChinese = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        return new ZZZGachaItem
        {
            Uid = uid,
            Id = item.Id,
            GachaType = gachaType,
            Name = item.ItemName,
            ItemType = item.ItemType.ToUpperInvariant() switch
            {
                "ITEM_TYPE_AVATAR" => isChinese ? "代理人" : item.ItemType,
                "ITEM_TYPE_WEAPON" => isChinese ? "音擎" : item.ItemType,
                "ITEM_TYPE_BANGBOO" => isChinese ? "邦布" : item.ItemType,
                _ => item.ItemType,
            },
            RankType = rankType,
            Time = item.Date,
            ItemId = item.ItemId,
            Count = 1,
            Lang = language,
        };
    }




    private class UIGFObj
    {
        public UIGFObj() { }

        public UIGFObj(long uid, List<ZZZGachaItem> list)
        {
            this.info = new UIGFInfo(uid, list);
            this.list = list;
        }

        public UIGFInfo info { get; set; }

        public List<ZZZGachaItem> list { get; set; }
    }


    private class UIGFInfo
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
        public long uid { get; set; }

        public string lang { get; set; }

        public int region_time_zone { get; set; } = 0;

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long export_timestamp { get; set; }

        public string export_app { get; set; } = "Starward";

        public string export_app_version { get; set; } = AppConfig.AppVersion ?? "";

        public string uigf_version { get; set; } = "v1.0";

        public UIGFInfo() { }

        public UIGFInfo(long uid, List<ZZZGachaItem> list)
        {
            this.uid = uid;
            lang = list.FirstOrDefault()?.Lang ?? "";
            export_timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
        }
    }


}
