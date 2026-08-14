using Dapper;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.Gacha;
using Starward.Core.Gacha.Kuro;
using Starward.Core.Games;
using Starward.Core.Games.Kuro;
using Starward.Features.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Gacha;

/// <summary>
/// 鸣潮的唤取记录。
/// <para/>
/// 与米哈游的差别都在 <see cref="KuroGachaClient"/> 里，这里只多做一件事：
/// 因为记录没有服务器端 ID，每次都是整池取回，不能按 endId 增量获取。
/// </summary>
internal class WuwaGachaService : GachaLogService
{


    /// <summary>
    /// 这里的 GameBiz 是存储键而不是米哈游的业务标识，
    /// 由 <see cref="GameKeyResolver.ToSettingsKey"/> 算出，与配置、数据库里的键一致。
    /// </summary>
    protected override GameBiz CurrentGameBiz { get; } = GameKeyResolver.ToSettingsKey(KuroGameMapping.WutheringWavesGlobal);

    protected override string GachaTableName { get; } = "WuwaGachaItem";



    public WuwaGachaService(ILogger<WuwaGachaService> logger, KuroGachaClient client) : base(logger, client)
    {

    }



    protected override List<GachaLogItemEx> GetGachaLogItemsByQueryType(IEnumerable<GachaLogItemEx> items, IGachaType type)
    {
        return items.Where(x => x.GachaType == type.Value).ToList();
    }



    /// <summary>
    /// 鸣潮的记录没有服务器端 ID，<paramref name="all"/> 没有意义，永远整池取回。
    /// 合成的 ID 是稳定的，重复获取只会覆盖同样的记录，不会产生重复。
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
            INSERT OR REPLACE INTO WuwaGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, Count, Lang)
            VALUES (@Uid, @Id, @Name, @Time, @ItemId, @ItemType, @RankType, @GachaType, @Count, @Lang);
            """, items, t);
        t.Commit();
        return affect;
    }



    /// <summary>
    /// UIGF 是米哈游生态的标准，鸣潮没有对应格式，这里导出自己的 JSON。
    /// </summary>
    public override async Task ExportGachaLogAsync(long uid, string file, string format)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<KuroGachaItem>($"SELECT * FROM {GachaTableName} WHERE Uid = @uid ORDER BY Id;", new { uid }).ToList();
        using var fs = System.IO.File.Create(file);
        await JsonSerializer.SerializeAsync(fs, new SimpleGachaExportFile<KuroGachaItem> { Uid = uid, List = list }, AppConfig.JsonSerializerOptions);
    }



    /// <summary>
    /// 没有可导入的公共格式，界面上也不提供入口
    /// </summary>
    public override long ImportGachaLog(string file)
    {
        throw new NotSupportedException("Wuthering Waves gacha records have no interchange format to import.");
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
