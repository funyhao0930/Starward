using Dapper;
using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.Gacha;
using Starward.Core.Gacha.Hotta;
using Starward.Core.Games;
using Starward.Core.Games.Hotta;
using Starward.Features.Database;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Gacha;

/// <summary>
/// 异环的斯卡布罗集市记录。
/// <para/>
/// 与其他游戏反过来：别家是「从游戏里取」，导入只是附带的；
/// 异环没有抽卡接口（记录走游戏自己的 RPC，没有网页版记录页，也就没有授权 URL 可用），
/// 所以<b>导入文件是唯一的入口</b>，取记录的那条路整条不通。
/// <para/>
/// 文件来自第三方抓包工具 nte-exporter，解析在 <see cref="HottaGachaClient.ReadExportFile"/>。
/// </summary>
internal class NteGachaService : GachaLogService
{


    /// <summary>
    /// 这里的 GameBiz 是存储键而不是米哈游的业务标识，
    /// 由 <see cref="GameKeyResolver.ToSettingsKey"/> 算出，与配置、数据库里的键一致。
    /// </summary>
    protected override GameBiz CurrentGameBiz { get; } = GameKeyResolver.ToSettingsKey(HottaGameMapping.NevernessToEvernessTaiwan);

    protected override string GachaTableName { get; } = "NteGachaItem";



    public NteGachaService(ILogger<NteGachaService> logger, HottaGachaClient client) : base(logger, client)
    {

    }



    protected override List<GachaLogItemEx> GetGachaLogItemsByQueryType(IEnumerable<GachaLogItemEx> items, IGachaType type)
    {
        return items.Where(x => x.GachaType == type.Value).ToList();
    }



    /// <summary>
    /// 保底规则。
    /// <para/>
    /// 两个棋盘是 90 抽保底、70 抽起进软保底；
    /// 弧盘奇迹盒 60 抽必出 S 级弧盘，软保底没有公开数字，取保底前的最后一轮十连。
    /// <para/>
    /// 神秘盒子没查到公开的保底数字，这里返回 null。注意 null 不等于「不画进度条」，
    /// 而是让界面退回按卡池编号推断的那套，也就是 90 抽——那根进度条对这个池子
    /// 没有意义，只是没有更好的东西可填；等查到真实数字再补上。
    /// </summary>
    protected override (int PityMax, int SoftPity)? GetPityRule(IGachaType type) => type.Value switch
    {
        HottaGachaType.StandardBoard or HottaGachaType.LimitedCharacterBoard => (90, 70),
        HottaGachaType.ArcMiracleBox => (60, 50),
        _ => null,
    };



    /// <summary>
    /// 没有接口可取，界面上这条路已经隐藏，真被调用到时明确失败而不是静默无事
    /// </summary>
    public override Task<long> GetGachaLogAsync(string url, bool all, string? lang = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Neverness to Everness has no gacha record API; import a file exported by nte-exporter instead.");
    }



    /// <summary>
    /// 本机没有可以翻出 URL 的缓存
    /// </summary>
    public override string? GetGachaLogUrlFromWebCache(string? installPath)
    {
        return null;
    }



    protected override int InsertGachaLogItems(List<GachaLogItem> items)
    {
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        int affect = dapper.Execute("""
            INSERT OR REPLACE INTO NteGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, Count, Lang)
            VALUES (@Uid, @Id, @Name, @Time, @ItemId, @ItemType, @RankType, @GachaType, @Count, @Lang);
            """, items, t);
        t.Commit();
        return affect;
    }



    /// <summary>
    /// 一份导入的结果，供界面汇报
    /// </summary>
    /// <param name="Uid">记录归属的玩家 uid</param>
    /// <param name="Total">读到的抽卡记录数</param>
    /// <param name="Added">其中新增的条数</param>
    /// <param name="Warnings">抓包工具自己报告的问题，原样转给用户</param>
    public record NteGachaImportResult(long Uid, int Total, int Added, List<string> Warnings);



    /// <summary>
    /// 导入若干份 nte-exporter 的导出文件。
    /// <para/>
    /// 该工具一个卡池写一个文件，玩家手上通常是四份，因此一次收一批。
    /// <paramref name="fallbackUid"/> 用于文件里没有 uid 的情况：抓包时没认出账号的导出
    /// 不带任何账号信息，只要同一批里有别的文件写了 uid 就归到那个账号，都没写才落到界面上选中的 uid。
    /// </summary>
    public NteGachaImportResult ImportGachaLogFiles(IReadOnlyList<string> files, long fallbackUid = 0)
    {
        // 先全部读出来，再决定这一批属于哪个账号。
        // 不能边读边定：文件的顺序就是用户在选择框里点出来的顺序，
        // 没有 uid 的那份要是排在前面，就会拿 fallbackUid 把整批钉死，
        // 后面真正带 uid 的文件反而被当成「不同账号」，整批失败。
        var exports = files.Select(x => (Name: Path.GetFileName(x), Export: HottaGachaClient.ReadExportFile(x))).ToList();
        long[] fileUids = exports.Select(x => x.Export.Uid).Where(x => x is not 0).Distinct().ToArray();
        if (fileUids.Length > 1)
        {
            // 一次导入只处理一个账号，混着来会把两个人的记录并在一起
            throw new ArgumentException(Lang.NteGachaService_TheSelectedFilesBelongToDifferentUids);
        }
        long uid = fileUids.Length is 1 ? fileUids[0] : fallbackUid;
        if (uid is 0)
        {
            // 所有文件都没写 uid，界面上也没有可以归属的账号，无从下手
            throw new ArgumentException(Lang.NteGachaService_TheExportFileDoesNotContainAUid);
        }

        var items = new List<GachaLogItem>();
        var warnings = new List<string>();
        foreach ((string name, HottaGachaClient.HottaGachaExport export) in exports)
        {
            foreach (HottaGachaItem item in export.Items)
            {
                item.Uid = uid;
                items.Add(item);
            }
            warnings.AddRange(export.Warnings.Select(x => $"{name}: {x}"));
            if (export.DroppedCount > 0)
            {
                // 认不出的卡池或读不了的时间戳，导入结果会少东西，不能只是默默丢掉
                warnings.Add($"{name}: {string.Format(Lang.NteGachaService_SomeRecordsCouldNotBeRead, export.DroppedCount)}");
            }
        }
        if (items.Count is 0)
        {
            throw new ArgumentException(Lang.NteGachaService_TheExportFileContainsNoGachaRecords);
        }
        using var dapper = DatabaseService.CreateConnection();
        int oldCount = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {GachaTableName} WHERE Uid = @Uid;", new { Uid = uid });
        InsertGachaLogItems(items);
        int newCount = dapper.QueryFirstOrDefault<int>($"SELECT COUNT(*) FROM {GachaTableName} WHERE Uid = @Uid;", new { Uid = uid });
        _logger.LogInformation("Imported {Count} Neverness to Everness gacha records of uid {Uid}, {Added} added.", items.Count, uid, newCount - oldCount);
        return new NteGachaImportResult(uid, items.Count, newCount - oldCount, warnings);
    }



    /// <summary>
    /// 单份导入，文件里必须带 uid
    /// </summary>
    public override long ImportGachaLog(string file)
    {
        return ImportGachaLogFiles([file]).Uid;
    }



    /// <summary>
    /// UIGF 是米哈游生态的标准，异环没有对应格式，这里导出自己的 JSON。
    /// </summary>
    public override async Task ExportGachaLogAsync(long uid, string file, string format)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<HottaGachaItem>($"SELECT * FROM {GachaTableName} WHERE Uid = @uid ORDER BY Id;", new { uid }).ToList();
        using var fs = System.IO.File.Create(file);
        await JsonSerializer.SerializeAsync(fs, new SimpleGachaExportFile<HottaGachaItem> { Uid = uid, List = list }, AppConfig.JsonSerializerOptions);
    }



    /// <summary>
    /// 没有物品图鉴接口，名称与稀有度都由导出文件一并带来
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
