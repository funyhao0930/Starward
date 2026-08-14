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
            INSERT OR REPLACE INTO EndfieldGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, Count, Lang)
            VALUES (@Uid, @Id, @Name, @Time, @ItemId, @ItemType, @RankType, @GachaType, @Count, @Lang);
            """, items, t);
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
