using Dapper;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.Gacha;
using Starward.Core.Gacha.Gryphline;
using Starward.Core.Games;
using Starward.Core.Games.Gryphline;
using Starward.Features.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Gacha;

/// <summary>
/// 明日方舟：终末地的寻访记录。
/// <para/>
/// 记录带服务器端的 seqId，去重不成问题；但接口的游标只能往更早翻，
/// 没有「只取更新的部分」的用法，因此每次都是整池取回。
/// </summary>
internal class EndfieldGachaService : GachaLogService
{


    /// <summary>
    /// 这里的 GameBiz 是存储键而不是米哈游的业务标识，
    /// 由 <see cref="GameKeyResolver.ToSettingsKey"/> 算出，与配置、数据库里的键一致。
    /// </summary>
    protected override GameBiz CurrentGameBiz { get; } = GameKeyResolver.ToSettingsKey(GryphlineGameMapping.EndfieldDefault);

    protected override string GachaTableName { get; } = "EndfieldGachaItem";


    /// <summary>
    /// 终末地最高 6 星，保底与统计都以它为准
    /// </summary>
    protected override int TopRankType => 6;



    public EndfieldGachaService(ILogger<EndfieldGachaService> logger, GryphlineGachaClient client) : base(logger, client)
    {

    }



    protected override List<GachaLogItemEx> GetGachaLogItemsByQueryType(IEnumerable<GachaLogItemEx> items, IGachaType type)
    {
        return items.Where(x => x.GachaType == type.Value).ToList();
    }



    /// <summary>
    /// 保底规则。不给的话界面会退回按卡池编号推断的那一套，终末地会被当成 90 抽，进度条的分母就错了。
    /// <para/>
    /// 干员池：6 星基础概率 0.8%，第 65 抽起每抽递增 5 个百分点，80 抽内必出 6 星，
    /// 因此取 80、软保底取递增起点 65。这里数的是 80 抽的小保底，它在卡池结束时
    /// 会继承到后续同类型卡池，所以不按卡池期清零；不继承的是 120 抽的大保底，
    /// 界面目前不显示它。
    /// <para/>
    /// 武库申领：6 星武器基础概率 4%，最多 4 次申领（40 抽）必出 6 星。没有公开的
    /// 递增数字，软保底同取 40，也就是一路绿到保底那一抽。
    /// </summary>
    protected override (int PityMax, int SoftPity)? GetPityRule(IGachaType type) => type.Value switch
    {
        GryphlineGachaType.Weapon => (40, 40),
        _ => (80, 65),
    };



    /// <summary>
    /// 福利十连完全在保底系统之外：不累加墊抽，就算出了 6 星也不把墊抽归零。
    /// <para/>
    /// 限定池累计 30 抽会赠送一次本期的十连，官方说明是「不计入任何保底计数」，
    /// 既不推进 80 抽小保底，也不算进 120 抽大保底。不这么处理的话墊抽会多算，
    /// 实测记录里因此出现过第 82 抽才出 6 星——超过了 80 抽的上限。
    /// <para/>
    /// 已知的不足：累计 60 抽赠送的那张十连券要下一期才能用，而它是<b>计入</b>
    /// 下一期保底的。两种赠送在记录里都只有 <c>isFree</c> 这一个标记，用掉之后
    /// 又都落在同一期卡池里，分不出来，因此那一次会少算 10 抽。少算只会让墊抽
    /// 偏小，不会算出超过保底上限的数字，比多算安全。
    /// </summary>
    protected override bool CountsForPity(GachaLogItemEx item) => !item.IsFree;



    /// <summary>
    /// 接口只能从最新往回翻，<paramref name="all"/> 没有意义，永远整池取回。
    /// seqId 是服务器给的，重复获取只会覆盖同样的记录。
    /// </summary>
    public override async Task<long> GetGachaLogAsync(string url, bool all, string? lang = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        using var dapper = DatabaseService.CreateConnection();
        // 正在获取 uid
        progress?.Report(Lang.GachaLogService_GettingUid);
        long uid = await _client.GetUidByGachaUrlAsync(url);
        if (uid == 0)
        {
            // 该账号最近6个月没有抽卡记录
            progress?.Report(Lang.GachaLogService_ThisAccountHasNoGachaRecordsInTheLast6Months);
            return uid;
        }
        var internalProgress = new Progress<(IGachaType GachaType, int Page)>((x) => progress?.Report(string.Format(Lang.GachaLogService_GetGachaProgressText, x.GachaType.ToLocalization(), x.Page)));
        var list = (await _client.GetGachaLogAsync(url, 0, lang, internalProgress, cancellationToken)).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        int oldCount = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {GachaTableName} WHERE Uid = @Uid;", new { Uid = uid });
        InsertGachaLogItems(list);
        int newCount = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {GachaTableName} WHERE Uid = @Uid;", new { Uid = uid });
        // 获取 {list.Count} 条记录，新增 {newCount - oldCount} 条记录
        progress?.Report(string.Format(Lang.GachaLogService_GetGachaResult, list.Count, newCount - oldCount));
        return uid;
    }



    protected override int InsertGachaLogItems(List<GachaLogItem> items)
    {
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        int affect = dapper.Execute("""
            INSERT OR REPLACE INTO EndfieldGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, Count, Lang, PoolId, IsFree)
            VALUES (@Uid, @Id, @Name, @Time, @ItemId, @ItemType, @RankType, @GachaType, @Count, @Lang, @PoolId, @IsFree);
            """, items.OfType<GryphlineGachaItem>().ToList(), t);
        t.Commit();
        return affect;
    }



    /// <summary>
    /// UIGF 是米哈游生态的标准，终末地没有对应格式，这里导出自己的 JSON。
    /// </summary>
    public override async Task ExportGachaLogAsync(long uid, string file, string format)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<GryphlineGachaItem>($"SELECT * FROM {GachaTableName} WHERE Uid = @uid ORDER BY Id;", new { uid }).ToList();
        using var fs = System.IO.File.Create(file);
        await JsonSerializer.SerializeAsync(fs, new SimpleGachaExportFile<GryphlineGachaItem> { Uid = uid, List = list }, AppConfig.JsonSerializerOptions);
    }



    /// <summary>
    /// 没有可导入的公共格式，界面上也不提供入口
    /// </summary>
    public override long ImportGachaLog(string file)
    {
        throw new NotSupportedException("Arknights: Endfield gacha records have no interchange format to import.");
    }



    /// <summary>
    /// 没有物品图鉴接口，名称与稀有度都由记录接口一并返回
    /// </summary>
    public override Task<string> UpdateGachaInfoAsync(GameBiz gameBiz, string lang, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(lang);
    }


    public override Task<(string Language, int Count)> ChangeGachaItemNameAsync(GameBiz gameBiz, string lang, CancellationToken cancellationToken = default)
    {
        return Task.FromResult((lang, 0));
    }


}
