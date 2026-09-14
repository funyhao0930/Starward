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
    /// 干员池：6 星基础概率 0.8%，第 65 抽起每抽递增 5 个百分点，因此概率在第 85 抽
    /// 前后满 100%。玩家口中的「80 抽保底」不是上限：实测记录里出现过同一期卡池内
    /// 间隔 82 抽才出 6 星，所以这里取 85，软保底取递增起点 65。
    /// <para/>
    /// 武库申领：6 星武器基础概率 4%，最多 4 次申领（40 抽）必出 6 星。没有公开的
    /// 递增数字，软保底同取 40，也就是一路绿到保底那一抽。
    /// </summary>
    protected override (int PityMax, int SoftPity)? GetPityRule(IGachaType type) => type.Value switch
    {
        GryphlineGachaType.Weapon => (40, 40),
        _ => (85, 65),
    };



    /// <summary>
    /// 换一期卡池就重新数墊抽。
    /// <para/>
    /// 特许寻访是一个卡池编号底下的许多期，官方写明保底「在寻访关闭时清零，
    /// 不会继承」，只看编号会把各期连成一串，算出超过保底上限的墊抽数。
    /// 基础寻访这类常驻池的 poolId 始终不变，因此这条规则对所有池子都成立。
    /// <para/>
    /// poolId 是后来才存的，旧记录整列为空，这时所有记录的 poolId 相等，
    /// 行为与从前一致；重新获取一次记录就会补上。
    /// </summary>
    protected override bool IsPityReset(GachaLogItemEx previous, GachaLogItemEx current)
    {
        return previous.PoolId != current.PoolId;
    }



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
            INSERT OR REPLACE INTO EndfieldGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, Count, Lang, PoolId)
            VALUES (@Uid, @Id, @Name, @Time, @ItemId, @ItemType, @RankType, @GachaType, @Count, @Lang, @PoolId);
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
