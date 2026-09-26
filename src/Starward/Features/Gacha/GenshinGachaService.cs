using Dapper;
using Microsoft.Extensions.Logging;
using MiniExcelLibs;
using Starward.Core;
using Starward.Core.Gacha;
using Starward.Core.Gacha.Genshin;
using Starward.Features.Database;
using Starward.Features.Gacha.UIGF;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Gacha;

internal class GenshinGachaService : GachaLogService
{



    protected override GameBiz CurrentGameBiz { get; } = GameBiz.hk4e;

    protected override string GachaTableName { get; } = "GenshinGachaItem";




    public GenshinGachaService(ILogger<GenshinGachaService> logger, GenshinGachaClient client) : base(logger, client)
    {

    }



    protected override List<GachaLogItemEx> GetGachaLogItemsByQueryType(IEnumerable<GachaLogItemEx> items, IGachaType type)
    {
        return type.Value switch
        {
            GenshinGachaType.CharacterEventWish => items.Where(x => x.GachaType == GenshinGachaType.CharacterEventWish || x.GachaType == GenshinGachaType.CharacterEventWish_2).ToList(),
            _ => items.Where(x => x.GachaType == type.Value).ToList(),
        };
    }


    public override List<GachaLogItemEx> GetGachaLogItemEx(long uid)
    {
        using var dapper = DatabaseService.CreateConnection();
        var list = dapper.Query<GachaLogItemEx>("""
            SELECT item.*, info.Icon FROM GenshinGachaItem item LEFT JOIN GenshinGachaInfo info ON item.ItemId=info.Id WHERE Uid=@uid ORDER BY item.Id;
            """, new { uid }).ToList();
        foreach (IGachaType type in QueryGachaTypes)
        {
            var l = GetGachaLogItemsByQueryType(list, type);
            int index = 0;
            int pity = 0;
            bool hasNoUp = GachaNoUp.TryGet(CurrentGameBiz, type.Value, out GachaNoUp? noUp);
            foreach (var item in l)
            {
                item.Index = ++index;
                item.Pity = ++pity;
                if (item.RankType == 5)
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


    protected override int InsertGachaLogItems(List<GachaLogItem> items)
    {
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        var affect = dapper.Execute("""
            INSERT OR REPLACE INTO GenshinGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, Count, Lang)
            VALUES (@Uid, @Id, @Name, @Time, @ItemId, @ItemType, @RankType, @GachaType, @Count, @Lang);
            """, items, t);
        t.Commit();
        UpdateGachaItemId();
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
        var list = dapper.Query<UIGFGenshinGachaItem>($"SELECT * FROM {GachaTableName} WHERE Uid = @uid ORDER BY Id;", new { uid }).ToList();
        foreach (var item in list)
        {
            item.UIGFGachaType = item.GachaType switch
            {
                400 => 301,
                _ => item.GachaType,
            };
        }
        DateTimeOffset time = DateTimeOffset.Now;
        var uigfObj = new UIGF3File<UIGFGenshinGachaItem>
        {
            Info = new UIAF3FileInfo
            {
                Uid = uid,
                Lang = list.Last().Lang ?? "",
                ExportTimestamp = time.ToUnixTimeSeconds(),
                ExportTime = time.ToString("yyyy-MM-dd HH:mm:ss"),
                ExportAppVersion = AppConfig.AppVersion,
                RegionTimeZone = uid.ToString()[0] switch
                {
                    '6' => -5,
                    '7' => 1,
                    _ => 8,
                },
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
        var template = Path.Combine(AppContext.BaseDirectory, @"Assets\Template\GachaLog.xlsx");
        if (File.Exists(template))
        {
            await MiniExcel.SaveAsByTemplateAsync(output, template, new { list });
        }
    }



    public override long ImportGachaLog(string file)
    {
        var str = File.ReadAllText(file);
        var obj = JsonSerializer.Deserialize<UIGF3File<UIGFGenshinGachaItem>>(str);
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
            // 成功导入祈愿记录 {count} 条
            InAppToast.MainWindow?.Success($"Uid {obj.Info.Uid}", string.Format(Lang.GenshinGachaService_ImportWishRecordsSuccessfully, count), 5000);
            return obj.Info.Uid;
        }
        return 0;
    }



    public override async Task<string> UpdateGachaInfoAsync(GameBiz gameBiz, string lang, CancellationToken cancellationToken = default)
    {
        var data = await _client.GetGenshinGachaInfoAsync(gameBiz, lang, cancellationToken);
        using var dapper = DatabaseService.CreateConnection();
        using var t = dapper.BeginTransaction();
        const string insertSql = """
            INSERT OR REPLACE INTO GenshinGachaInfo (Id, Name, Icon, Element, Level, CatId, WeaponCatId)
            VALUES (@Id, @Name, @Icon, @Element, @Level, @CatId, @WeaponCatId);
            """;
        dapper.Execute(insertSql, data.AllAvatar, t);
        dapper.Execute(insertSql, data.AllWeapon, t);
        t.Commit();
        UpdateGachaItemId();
        await UpdateGachaItemIdByRecordLanguageAsync(gameBiz, data.Language, cancellationToken);
        return data.Language;
    }


    /// <summary>
    /// 补上其他语言的记录的 ItemId。
    /// <para/>
    /// 有些来源的记录不带 item_id（旧版导出、部分工具的 UIGF），存进来是 0，只能按名称对回图鉴。
    /// 但图鉴只存当前界面语言的名称：先用简体抓的记录，界面换成繁体之后就再也对不上，
    /// 这些记录的图示也就一直是空的。这里按记录自己的语言另取一份图鉴，只拿来对名称，
    /// 不写进图鉴表，免得把当前语言的名称盖掉。
    /// <para/>
    /// 同一个名称对应多个 ID 的（旅行者的两种性别）无法判断，跳过；它也不会从卡池里出来。
    /// </summary>
    private bool _itemIdFilled;


    /// <summary>
    /// 原神的图示随图鉴一起更新，没有另外的对照表；但刚补上 ItemId 的记录现在有图了，
    /// 页面得重画才看得到。页面在 <see cref="UpdateGachaInfoAsync"/> 之后紧接着调用这里。
    /// </summary>
    public override Task<bool> UpdateGachaIconsAsync(CancellationToken cancellationToken = default)
    {
        bool filled = _itemIdFilled;
        _itemIdFilled = false;
        return Task.FromResult(filled);
    }


    private async Task UpdateGachaItemIdByRecordLanguageAsync(GameBiz gameBiz, string infoLanguage, CancellationToken cancellationToken)
    {
        using var dapper = DatabaseService.CreateConnection();
        var langs = dapper.Query<string>("SELECT DISTINCT Lang FROM GenshinGachaItem WHERE ItemId = 0 AND Lang IS NOT NULL AND Lang <> '';")
                          .Where(x => LanguageUtil.FilterLanguage(x) != infoLanguage)
                          .ToList();
        foreach (string lang in langs)
        {
            try
            {
                var wiki = await _client.GetGenshinGachaInfoAsync(gameBiz, lang, cancellationToken);
                var ids = wiki.AllAvatar.Concat(wiki.AllWeapon)
                              .GroupBy(x => x.Name)
                              .Where(x => x.Select(y => y.Id).Distinct().Count() == 1)
                              .Select(x => new { Name = x.Key, x.First().Id, Lang = lang })
                              .ToList();
                using var t = dapper.BeginTransaction();
                if (dapper.Execute("UPDATE GenshinGachaItem SET ItemId = @Id WHERE ItemId = 0 AND Name = @Name AND Lang = @Lang;", ids, t) > 0)
                {
                    _itemIdFilled = true;
                }
                t.Commit();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Update item id of {lang} gacha records", lang);
            }
        }
    }


    private void UpdateGachaItemId()
    {
        using var dapper = DatabaseService.CreateConnection();
        dapper.Execute("""
            INSERT OR REPLACE INTO GenshinGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, Count, Lang)
            SELECT item.Uid, item.Id, item.Name, Time, info.Id, ItemType, RankType, GachaType, Count, Lang
            FROM GenshinGachaItem item INNER JOIN GenshinGachaInfo info ON item.Name = info.Name WHERE item.ItemId = 0;
            """);
    }


    public override async Task<(string Language, int Count)> ChangeGachaItemNameAsync(GameBiz gameBiz, string lang, CancellationToken cancellationToken = default)
    {
        lang = await UpdateGachaInfoAsync(gameBiz, lang, cancellationToken);
        using var dapper = DatabaseService.CreateConnection();
        int count = dapper.Execute("""
            INSERT OR REPLACE INTO GenshinGachaItem (Uid, Id, Name, Time, ItemId, ItemType, RankType, GachaType, Count, Lang)
            SELECT item.Uid, item.Id, info.Name, Time, ItemId, ItemType, RankType, GachaType, Count, @Lang
            FROM GenshinGachaItem item INNER JOIN GenshinGachaInfo info ON item.ItemId = info.Id;
            """, new { Lang = lang });
        return (lang, count);
    }


}
