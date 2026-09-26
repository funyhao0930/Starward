using Dapper;
using Microsoft.Extensions.Logging;
using MiniExcelLibs;
using Starward.Core;
using Starward.Core.Gacha;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.StarRail;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Core.Games.Hotta;
using Starward.Core.Games.Kuro;
using Starward.Features.Database;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Gacha;


internal abstract class GachaLogService
{


    protected readonly ILogger<GachaLogService> _logger;


    protected readonly GachaLogClient _client;


    protected GachaLogService(ILogger<GachaLogService> logger, GachaLogClient client)
    {
        _logger = logger;
        _client = client;
    }



    protected abstract GameBiz CurrentGameBiz { get; }

    protected abstract string GachaTableName { get; }

    protected abstract List<GachaLogItemEx> GetGachaLogItemsByQueryType(IEnumerable<GachaLogItemEx> items, IGachaType type);

    public IReadOnlyCollection<IGachaType> QueryGachaTypes => _client.QueryGachaTypes;


    /// <summary>
    /// 最高稀有度，也就是「保底」在数的那一档。
    /// <para/>
    /// 米哈游三款是 5 星，终末地是 6 星，绝区零内部存 4（S 级）。
    /// 统计与保底计算都读这三个属性，而不是写死 5/4/3。
    /// </summary>
    protected virtual int TopRankType => 5;

    /// <summary>
    /// 次高稀有度，界面上的第二排
    /// </summary>
    protected virtual int SecondRankType => TopRankType - 1;

    /// <summary>
    /// 第三档稀有度
    /// </summary>
    protected virtual int ThirdRankType => TopRankType - 2;


    /// <summary>
    /// 卡池的保底规则：保底抽数与软保底起点。
    /// <para/>
    /// 返回 null 表示沿用 <see cref="GachaLogItemEx.Progress"/> 里按卡池编号推断的老规则，
    /// 米哈游三款都走这条路。其他厂商的卡池编号会与它们相撞，必须自己给出。
    /// </summary>
    protected virtual (int PityMax, int SoftPity)? GetPityRule(IGachaType type) => null;


    /// <summary>
    /// 这一条算不算进保底计数。默认都算，一条记录就是一抽。
    /// <para/>
    /// 不算的记录既不累加墊抽，出了最高稀有度也不把墊抽归零——它完全在保底
    /// 系统之外。终末地的福利十连就是这样，见 <see cref="EndfieldGachaService"/>。
    /// </summary>
    protected virtual bool CountsForPity(GachaLogItemEx item) => true;



    #region 物品图示


    /// <summary>
    /// 物品图示对照表，列为 ItemId、Key、Icon。
    /// <para/>
    /// 米哈游三款的图示跟着图鉴一起存在各自的 Info 表里，不走这里，保持 null。
    /// 其他厂商的记录接口不给图，图示另外从社群的数据站取，存进这张表，
    /// 读取记录时由 <see cref="FillGachaIcons"/> 按 ItemId 补上。
    /// </summary>
    protected virtual string? GachaIconTableName => null;


    /// <summary>
    /// 更新物品图示对照表，返回对照表有没有变。变了的话界面要重画一次，
    /// 否则第一次打开时的空白图示要等到下次打开才会出现。
    /// </summary>
    public virtual Task<bool> UpdateGachaIconsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }


    /// <summary>
    /// 各游戏共用的更新流程：数据源的版本没变就什么都不做，变了才整表替换。
    /// </summary>
    /// <param name="fetch">传入已知版本（对照表为空时是 null），版本相同时返回的列表为 null</param>
    protected async Task<bool> UpdateGachaIconsAsync(Func<string?, CancellationToken, Task<(string Version, List<GachaItemIcon>? Icons)>> fetch, CancellationToken cancellationToken)
    {
        string table = GachaIconTableName ?? throw new InvalidOperationException($"{GetType().Name} has no gacha icon table.");
        string versionKey = $"{table}Version";
        using var dapper = DatabaseService.CreateConnection();
        string? knownVersion = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {table};") > 0
            ? DatabaseService.GetValue<string>(versionKey, out _)
            : null;
        (string version, List<GachaItemIcon>? icons) = await fetch(knownVersion, cancellationToken);
        if (icons is null)
        {
            return false;
        }
        using var t = dapper.BeginTransaction();
        dapper.Execute($"DELETE FROM {table};", transaction: t);
        dapper.Execute($"INSERT OR REPLACE INTO {table} (ItemId, Key, Icon) VALUES (@ItemId, @Key, @Icon);", icons, t);
        t.Commit();
        DatabaseService.SetValue(versionKey, version);
        return true;
    }


    /// <summary>
    /// 按 ItemId 补上图示。对照表还没有或查不到的物品保持空白，界面会画稀有度占位块。
    /// </summary>
    protected void FillGachaIcons(List<GachaLogItemEx> list)
    {
        if (GachaIconTableName is not string table || list.Count == 0)
        {
            return;
        }
        using var dapper = DatabaseService.CreateConnection();
        var icons = dapper.Query<(int ItemId, string Icon)>($"SELECT ItemId, Icon FROM {table};").ToDictionary(x => x.ItemId, x => x.Icon);
        foreach (var item in list)
        {
            if (icons.TryGetValue(item.ItemId, out string? icon))
            {
                item.Icon = icon;
            }
        }
    }


    #endregion



    /// <summary>
    /// 这款游戏自己对抽卡的叫法。
    /// <para/>
    /// 导航栏、抽卡页与占位页都显示它，因此只在这里写一份。
    /// </summary>
    public static string GetGachaLogText(GameKey key)
    {
        return key.GameId switch
        {
            GameBiz.hk4e => Lang.GachaLogService_WishRecords,
            GameBiz.hkrpg => Lang.GachaLogService_WarpRecords,
            GameBiz.nap => Lang.GachaLogService_SignalSearchRecords,
            KuroGameMapping.WutheringWaves => Lang.GachaLogService_ConveneRecords,
            GryphlineGameMapping.Endfield => Lang.GachaLogService_RecruitmentRecords,
            HottaGameMapping.NevernessToEverness => Lang.GachaLogService_ScarboroughFair,
            _ => ""
        };
    }



    public virtual List<long> GetUids()
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Query<long>($"SELECT DISTINCT Uid FROM {GachaTableName};").ToList();
    }



    public virtual List<GachaLogItemEx> GetGachaLogItemEx(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<GachaLogItemEx>($"SELECT * FROM {GachaTableName} WHERE Uid = @uid ORDER BY Id;", new { uid }).ToList();
        foreach (IGachaType type in QueryGachaTypes)
        {
            var l = GetGachaLogItemsByQueryType(list, type);
            (int PityMax, int SoftPity)? pityRule = GetPityRule(type);
            bool hasNoUp = GachaNoUp.TryGet(CurrentGameBiz, type.Value, out GachaNoUp? noUp);
            int index = 0;
            int pity = 0;
            foreach (var item in l)
            {
                item.Index = ++index;
                bool countsForPity = CountsForPity(item);
                if (countsForPity)
                {
                    pity++;
                }
                // 不算保底的记录显示当前的墊抽数，既不推进也不归零
                item.Pity = pity;
                item.PityMax = pityRule?.PityMax;
                item.SoftPity = pityRule?.SoftPity;
                if (item.RankType == TopRankType)
                {
                    if (countsForPity)
                    {
                        pity = 0;
                    }
                    item.HasUpItem = hasNoUp;
                    if (hasNoUp)
                    {
                        item.IsUp = noUp!.IsUp(item);
                    }
                }
            }
        }
        FillGachaIcons(list);
        return list;
    }



    /// <summary>
    /// 从本机文件中找出带授权信息的抽卡记录 URL。
    /// 藏在哪个文件、长什么样由各游戏的客户端决定。
    /// <para/>
    /// 这里必须由调用方给出带服务器的 GameBiz，不能用 <see cref="CurrentGameBiz"/>：
    /// 后者是这款游戏在设置与数据库里的键（如 hkrpg），而缓存文件的位置分国服、
    /// 国际服与 B 服（hkrpg_cn / hkrpg_global / hkrpg_bilibili）。
    /// </summary>
    public virtual string? GetGachaLogUrlFromWebCache(GameBiz gameBiz, string? installPath)
    {
        return _client.FindGachaUrlFromLocalFiles(gameBiz, installPath);
    }




    public virtual async Task<long> GetUidFromGachaLogUrl(string url)
    {
        long uid = await _client.GetUidByGachaUrlAsync(url);
        if (uid > 0)
        {
            using var dapper = DatabaseService.CreateConnection();
            dapper.Execute("INSERT OR REPLACE INTO GachaLogUrl (GameBiz, Uid, Url, Time) VALUES (@GameBiz, @Uid, @Url, @Time);", new GachaLogUrl(CurrentGameBiz, uid, url));
        }
        return uid;
    }



    public virtual string? GetGachaLogUrlByUid(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.QueryFirstOrDefault<string>("SELECT Url FROM GachaLogUrl WHERE Uid = @uid AND GameBiz = @GameBiz LIMIT 1;", new { uid, GameBiz = CurrentGameBiz });
    }



    protected abstract int InsertGachaLogItems(List<GachaLogItem> items);



    public virtual async Task<long> GetGachaLogAsync(string url, bool all, string? lang = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
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






    public virtual (List<GachaTypeStats> GachaStats, List<GachaLogItemEx> ItemStats) GetGachaTypeStats(long uid)
    {
        var statsList = new List<GachaTypeStats>();
        var groupStats = new List<GachaLogItemEx>();
        using var dapper = DatabaseService.CreateConnection();
        var allItems = GetGachaLogItemEx(uid);
        if (allItems.Count > 0)
        {
            foreach (IGachaType type in QueryGachaTypes)
            {
                var list = GetGachaLogItemsByQueryType(allItems, type);
                if (list.Count == 0)
                {
                    continue;
                }
                var stats = new GachaTypeStats
                {
                    GachaType = type.Value,
                    GachaTypeText = type.ToLocalization(),
                    Count = list.Count,
                    Count_5_Up = list.Count(x => x.RankType == TopRankType && x.IsUp),
                    Count_5 = list.Count(x => x.RankType == TopRankType),
                    Count_4 = list.Count(x => x.RankType == SecondRankType),
                    Count_3 = list.Count(x => x.RankType == ThirdRankType),
                    StartTime = list.First().Time,
                    EndTime = list.Last().Time
                };
                stats.Ratio_5 = (double)stats.Count_5 / stats.Count;
                stats.Ratio_4 = (double)stats.Count_4 / stats.Count;
                stats.Ratio_3 = (double)stats.Count_3 / stats.Count;
                stats.List_5 = list.Where(x => x.RankType == TopRankType).Reverse().ToList();
                stats.List_4 = list.Where(x => x.RankType == SecondRankType).Reverse().ToList();
                stats.Pity_5 = list.Last().Pity;
                // 不算保底的那一抽就算是最高稀有度，墊抽也没有归零
                if (list.Last().RankType == TopRankType && CountsForPity(list.Last()))
                {
                    stats.Pity_5 = 0;
                }
                stats.Average_5 = (double)(stats.Count - stats.Pity_5) / stats.Count_5;
                stats.Pity_4 = list.Skip(list.FindLastIndex(x => x.RankType == SecondRankType) + 1).Count(CountsForPity);

                if (stats.Count_5_Up > 0)
                {
                    int c = stats.Count - stats.Pity_5;
                    stats.Average_5_Up = (double)c / stats.Count_5_Up;
                }

                int pity_4 = 0;
                foreach (var item in list)
                {
                    bool countsForPity = CountsForPity(item);
                    if (countsForPity)
                    {
                        pity_4++;
                    }
                    if (item.RankType == SecondRankType)
                    {
                        item.Pity = pity_4;
                        if (countsForPity)
                        {
                            pity_4 = 0;
                        }
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
                    (int PityMax, int SoftPity)? pityRule = GetPityRule(type);
                    stats.List_5.Insert(0, new GachaLogItemEx
                    {
                        GachaType = type.Value,
                        Name = Lang.GachaStatsCard_Pity,
                        Pity = stats.Pity_5,
                        PityMax = pityRule?.PityMax,
                        SoftPity = pityRule?.SoftPity,
                        Time = list.Last().Time,
                        HasUpItem = GachaNoUp.TryGet(CurrentGameBiz, type.Value, out _),
                    });
                    stats.List_4.Insert(0, new GachaLogItemEx
                    {
                        GachaType = type.Value,
                        Name = Lang.GachaStatsCard_Pity,
                        Pity = stats.Pity_4,
                        PityMax = pityRule?.PityMax,
                        SoftPity = pityRule?.SoftPity,
                        Time = list.Last().Time,
                        HasUpItem = GachaNoUp.TryGet(CurrentGameBiz, type.Value, out _),
                    });
                }
            }
            groupStats = allItems.GroupBy(x => x.ItemId)
                                 .Select(x => { var item = x.First(); item.ItemCount = x.Count(); return item; })
                                 .OrderByDescending(x => x.RankType)
                                 .ThenByDescending(x => x.ItemCount)
                                 .ThenByDescending(x => x.Time)
                                 .ToList();
        }
        return (statsList, groupStats);
    }






    public virtual int DeleteUid(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Execute($"DELETE FROM {GachaTableName} WHERE Uid = @uid;", new { uid });
    }



    public virtual int DeleteGachaLogByTime(long uid, DateTime begin, DateTime end)
    {
        using var dapper = DatabaseService.CreateConnection();
        return dapper.Execute($"DELETE FROM {GachaTableName} WHERE Uid = @uid AND Time >= @begin AND Time <= @end;", new { uid, begin, end });
    }



    public abstract Task ExportGachaLogAsync(long uid, string file, string format);




    public abstract long ImportGachaLog(string file);




    public abstract Task<string> UpdateGachaInfoAsync(GameBiz gameBiz, string lang, CancellationToken cancellationToken = default);



    public abstract Task<(string Language, int Count)> ChangeGachaItemNameAsync(GameBiz gameBiz, string lang, CancellationToken cancellationToken = default);


}
