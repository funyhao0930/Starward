using Starward.Core.Gacha;
using Starward.Core.Gacha.Hotta;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Starward.Core.Tests.Gacha;

/// <summary>
/// 异环斯卡布罗集市记录的导入。
/// <para/>
/// 全部离线：异环没有抽卡接口，记录只能从第三方抓包工具 nte-exporter 的导出文件读进来，
/// 这里的 JSON 都是照该工具的格式手写的。
/// </summary>
public class HottaGachaClientTests
{


    /// <summary>
    /// 限定角色棋盘的一份导出。
    /// <para/>
    /// 记录从新到旧排；同一时间戳内 ordinal 0 是最新的；
    /// 中间夹着一条 points_gift 与一条 chase_reward，它们是落格附带的奖励，不是抽数。
    /// </summary>
    private const string LimitedBoardExport = """
        {
          "format": "nte-history-export",
          "format_version": 1,
          "game": "Neverness to Everness",
          "source": "live_capture",
          "banner": { "id": "Lottery_LimitedCharacter", "name": "Limited Character Board" },
          "scan": {
            "warnings": [
              { "code": "PAGE_GAP_DETECTED", "reason": "Page gap detected after page 5." }
            ]
          },
          "user_uid": "218216016349",
          "server_id": "23003",
          "records": [
            {
              "uid": "aaaa0000000000000000000000000001",
              "pool_group_id": "Lottery_LimitedCharacter",
              "timestamp": "2026-06-10 13:32:20",
              "timestamp_group_ordinal": 0,
              "roll_result": 6,
              "result_type": "dice",
              "reward_type": "character",
              "reward_id": "1072",
              "reward_name": "Linko",
              "reward_rank": "S",
              "quantity": 1
            },
            {
              "uid": "aaaa0000000000000000000000000002",
              "pool_group_id": "Lottery_LimitedCharacter",
              "timestamp": "2026-06-10 13:32:17",
              "timestamp_group_ordinal": 0,
              "roll_result": -4,
              "result_type": "chase_reward",
              "reward_type": "item",
              "reward_id": "Dice_ticket_01",
              "reward_name": "Warp Piece",
              "reward_rank": "S",
              "quantity": 30
            },
            {
              "uid": "aaaa0000000000000000000000000003",
              "pool_group_id": "Lottery_LimitedCharacter",
              "timestamp": "2026-06-10 13:32:17",
              "timestamp_group_ordinal": 1,
              "roll_result": 0,
              "result_type": "points_gift",
              "reward_type": "item",
              "reward_id": "Gold",
              "reward_name": "Beetle Coin",
              "reward_rank": "B",
              "quantity": 1
            },
            {
              "uid": "aaaa0000000000000000000000000004",
              "pool_group_id": "Lottery_LimitedCharacter",
              "timestamp": "2026-06-10 13:32:17",
              "timestamp_group_ordinal": 2,
              "roll_result": 3,
              "result_type": "dice",
              "reward_type": "item",
              "reward_id": "CharacterUpMaterial_lv3",
              "reward_name": "Elite Hunter Guide",
              "reward_rank": "A",
              "quantity": 1
            },
            {
              "uid": "aaaa0000000000000000000000000005",
              "pool_group_id": "Lottery_LimitedCharacter",
              "timestamp": "2026-06-10 13:32:17",
              "timestamp_group_ordinal": 3,
              "roll_result": 1,
              "result_type": "dice",
              "reward_type": "item",
              "reward_id": "CityAbility_UpMaterial",
              "reward_name": "Dreamless Seed",
              "reward_rank": "B",
              "quantity": 1
            }
          ]
        }
        """;


    /// <summary>
    /// 同一批记录抓得浅一些：少了那条 points_gift 与最旧的一抽，
    /// 剩下两条的 timestamp_group_ordinal 与完整版一致——这正是该工具保证的。
    /// </summary>
    private const string LimitedBoardPartialExport = """
        {
          "format": "nte-history-export",
          "format_version": 1,
          "banner": { "id": "Lottery_LimitedCharacter", "name": "Limited Character Board" },
          "user_uid": "218216016349",
          "records": [
            {
              "uid": "aaaa0000000000000000000000000001",
              "pool_group_id": "Lottery_LimitedCharacter",
              "timestamp": "2026-06-10 13:32:20",
              "timestamp_group_ordinal": 0,
              "result_type": "dice",
              "reward_type": "character",
              "reward_id": "1072",
              "reward_name": "Linko",
              "reward_rank": "S",
              "quantity": 1
            },
            {
              "uid": "aaaa0000000000000000000000000004",
              "pool_group_id": "Lottery_LimitedCharacter",
              "timestamp": "2026-06-10 13:32:17",
              "timestamp_group_ordinal": 2,
              "result_type": "dice",
              "reward_type": "item",
              "reward_id": "CharacterUpMaterial_lv3",
              "reward_name": "Elite Hunter Guide",
              "reward_rank": "A",
              "quantity": 1
            }
          ]
        }
        """;


    /// <summary>
    /// 弧盘奇迹盒的记录形状不一样：没有 result_type，认不出来的名字写 UNKNOWN、稀有度写空串
    /// </summary>
    private const string ArcExport = """
        {
          "format": "nte-history-export",
          "format_version": 1,
          "banner": { "id": "Arc_MiracleBox", "name": "Arc Miracle Box" },
          "user_uid": "218216016349",
          "records": [
            {
              "uid": "bbbb0000000000000000000000000001",
              "pool_group_id": "Arc_MiracleBox",
              "timestamp": "2026-07-01 09:00:00",
              "timestamp_group_ordinal": 0,
              "reward_type": "arc",
              "reward_id": "fork_nonos",
              "reward_name": "UNKNOWN",
              "reward_rank": "",
              "source_type": "miracle_box"
            }
          ]
        }
        """;



    [Fact]
    public void QueryGachaTypes_CoversTheFourPoolsAndAllHaveNames()
    {
        var client = new HottaGachaClient();

        Assert.Equal(4, client.QueryGachaTypes.Count);
        Assert.All(client.QueryGachaTypes, x => Assert.False(string.IsNullOrEmpty(x.ToLocalization())));
    }



    [Fact]
    public void ParseExport_ReadsUidAndWarnings()
    {
        var export = HottaGachaClient.ParseExport(LimitedBoardExport);

        Assert.Equal(218216016349, export.Uid);
        string warning = Assert.Single(export.Warnings);
        Assert.Contains("PAGE_GAP_DETECTED", warning);
    }



    /// <summary>
    /// 每一行都讀進來，但只有擲骰那幾行算一抽；附帶獎勵靠 ResultType 分辨
    /// </summary>
    [Fact]
    public void ParseExport_KeepsEveryRowAndTagsWhichAreRolls()
    {
        var export = HottaGachaClient.ParseExport(LimitedBoardExport);

        Assert.Equal(5, export.Items.Count);
        Assert.Equal(3, export.Items.Count(x => x.ResultType is "dice"));
        // 附帶獎勵要留著（角色幾乎都從積分來），但不是「一抽」
        Assert.Equal("chase_reward", export.Items.Single(x => x.Name is "Warp Piece").ResultType);
        Assert.Equal("points_gift", export.Items.Single(x => x.Name is "Beetle Coin").ResultType);
    }



    /// <summary>
    /// 文件里是从新到旧，界面与保底都按 Id 升序等于时间升序来算
    /// </summary>
    [Fact]
    public void ParseExport_OrdersRecordsFromOldToNew()
    {
        var export = HottaGachaClient.ParseExport(LimitedBoardExport);

        Assert.Equal(["Dreamless Seed", "Elite Hunter Guide", "Beetle Coin", "Warp Piece", "Linko"], export.Items.Select(x => x.Name));
        Assert.Equal(export.Items.OrderBy(x => x.Id).Select(x => x.Id), export.Items.Select(x => x.Id));
        Assert.Equal(export.Items.Count, export.Items.Select(x => x.Id).Distinct().Count());
    }



    [Fact]
    public void ParseExport_MapsLetterRanksToStarTiers()
    {
        var export = HottaGachaClient.ParseExport(LimitedBoardExport);

        Assert.Equal(5, export.Items.Single(x => x.Name is "Linko").RankType);
        Assert.Equal(4, export.Items.Single(x => x.Name is "Elite Hunter Guide").RankType);
        Assert.Equal(3, export.Items.Single(x => x.Name is "Dreamless Seed").RankType);
    }



    [Fact]
    public void ParseExport_UsesThePoolOfEachRecord()
    {
        var board = HottaGachaClient.ParseExport(LimitedBoardExport);
        var arc = HottaGachaClient.ParseExport(ArcExport);

        Assert.All(board.Items, x => Assert.Equal(HottaGachaType.LimitedCharacterBoard, x.GachaType));
        Assert.All(arc.Items, x => Assert.Equal(HottaGachaType.ArcMiracleBox, x.GachaType));
    }



    /// <summary>
    /// 弧盘奇迹盒每条都是一抽，认不出来的名字退回物品 ID，稀有度按未知处理
    /// </summary>
    [Fact]
    public void ParseExport_HandlesTheArcPoolShape()
    {
        var export = HottaGachaClient.ParseExport(ArcExport);

        HottaGachaItem item = Assert.Single(export.Items);
        Assert.Null(item.ResultType);
        Assert.Equal("fork_nonos", item.Name);
        Assert.Equal(0, item.RankType);
        Assert.Equal("arc", item.ItemType);
    }



    /// <summary>
    /// 同一份文件读两次必须得到同样的 ID，否则重复导入会插入重复记录
    /// </summary>
    [Fact]
    public void ParseExport_ProducesStableIds()
    {
        var first = HottaGachaClient.ParseExport(LimitedBoardExport);
        var second = HottaGachaClient.ParseExport(LimitedBoardExport);

        Assert.Equal(first.Items.Select(x => x.Id), second.Items.Select(x => x.Id));
    }



    /// <summary>
    /// 时间戳就是游戏里显示的时间，原样存，不再当成 UTC 换一次时区。
    /// 实测：游戏内写 2026/9/1 21:33:48，当成 UTC 转本地会变成 9/2 05:33:48，差了 8 小时。
    /// ID 仍与机器时区无关。
    /// </summary>
    [Fact]
    public void ParseExport_KeepsTheTimestampAsTheGameShowsIt()
    {
        var export = HottaGachaClient.ParseExport(ArcExport);

        HottaGachaItem item = Assert.Single(export.Items);
        Assert.Equal(new DateTime(2026, 7, 1, 9, 0, 0), item.Time);
        Assert.Equal(DateTimeKind.Unspecified, item.Time.Kind);
        DateTime key = new(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal(GachaSyntheticId.FromTime(key, 99, $"{HottaGachaType.ArcMiracleBox}|fork_nonos"), item.Id);
    }


    /// <summary>
    /// ID 不能掺进任何会变的东西：该工具的物品名来自一份会更新的对照表，
    /// 同一抽换个名字还算出同一个 ID，重复导入才只会覆盖而不是插入第二条。
    /// </summary>
    [Fact]
    public void ParseExport_IdDoesNotDependOnTheRewardName()
    {
        var before = HottaGachaClient.ParseExport(ArcExport);
        // 对照表更新后，同一份抓包会写出真正的名字
        var after = HottaGachaClient.ParseExport(ArcExport.Replace("\"UNKNOWN\"", "\"Nonos\""));

        Assert.Equal(before.Items.Select(x => x.Id), after.Items.Select(x => x.Id));
        Assert.Equal("fork_nonos", Assert.Single(before.Items).Name);
        Assert.Equal("Nonos", Assert.Single(after.Items).Name);
    }


    /// <summary>
    /// 抓得更浅不能改变已有记录的 ID：序号来自工具保证稳定的组内 ordinal，
    /// 而不是记录在文件里的位置。按位置数的话，少抓到中间一条就会让后面的全部错位。
    /// </summary>
    [Fact]
    public void ParseExport_IdSurvivesAShallowerCapture()
    {
        var full = HottaGachaClient.ParseExport(LimitedBoardExport);
        var partial = HottaGachaClient.ParseExport(LimitedBoardPartialExport);

        Assert.Equal(2, partial.Items.Count);
        foreach (HottaGachaItem item in partial.Items)
        {
            Assert.Equal(full.Items.Single(x => x.Name == item.Name).Id, item.Id);
        }
    }



    /// <summary>
    /// 工具没认出账号时整个 user_uid 字段都不写，得由上层决定归到哪个账号
    /// </summary>
    [Fact]
    public void ParseExport_ReturnsZeroUidWhenTheFileHasNone()
    {
        string json = LimitedBoardExport.Replace("\"user_uid\": \"218216016349\",", "");

        var export = HottaGachaClient.ParseExport(json);

        Assert.Equal(0, export.Uid);
        Assert.NotEmpty(export.Items);
    }



    /// <summary>
    /// 该工具改过好几次结构却从没升过 format_version，
    /// 所以不能拿版本号挡，多出来的字段也要能读
    /// </summary>
    [Fact]
    public void ParseExport_ToleratesNewerVersionsAndUnknownKeys()
    {
        string json = LimitedBoardExport.Replace("\"format_version\": 1", "\"format_version\": 9, \"something_new\": {\"a\": 1}");

        var export = HottaGachaClient.ParseExport(json);

        Assert.Equal(5, export.Items.Count);
    }



    [Fact]
    public void ParseExport_RejectsTheAchievementExport()
    {
        string json = """{"format":"nte-achievement-export","format_version":1,"categories":{}}""";

        Assert.Throws<ArgumentException>(() => HottaGachaClient.ParseExport(json));
    }



    [Theory]
    [InlineData("""{"format":"something-else","records":[]}""")]
    [InlineData("""{"hello":"world"}""")]
    [InlineData("not json at all")]
    public void ParseExport_RejectsForeignFiles(string json)
    {
        Assert.Throws<ArgumentException>(() => HottaGachaClient.ParseExport(json));
    }



    /// <summary>
    /// 认不出来的卡池不能悄悄并进常驻池，宁可跳过
    /// </summary>
    [Fact]
    public void ParseExport_SkipsRecordsOfAnUnknownPool()
    {
        string json = LimitedBoardExport.Replace("\"pool_group_id\": \"Lottery_LimitedCharacter\"", "\"pool_group_id\": \"Lottery_SomethingNew\"");

        var export = HottaGachaClient.ParseExport(json);

        Assert.Empty(export.Items);
        Assert.Equal(5, export.DroppedCount);
    }



    /// <summary>
    /// 整份文件都没写 ordinal（或写成 null）时，同一秒里的记录会全部当成 0。
    /// 这时不能再按 ordinal 算序号，否则同池同物品的两条会算出同一个 ID，
    /// 入库时被 INSERT OR REPLACE 覆盖掉一条。
    /// </summary>
    [Fact]
    public void ParseExport_FallsBackToPositionWhenOrdinalsCollide()
    {
        string json = """
            {
              "format": "nte-history-export",
              "banner": { "id": "Arc_MiracleBox" },
              "user_uid": "218216016349",
              "records": [
                { "pool_group_id": "Arc_MiracleBox", "timestamp": "2026-07-01 09:00:00", "reward_type": "arc", "reward_id": "fork_nonos", "reward_name": "Nonos", "reward_rank": "B" },
                { "pool_group_id": "Arc_MiracleBox", "timestamp": "2026-07-01 09:00:00", "timestamp_group_ordinal": null, "reward_type": "arc", "reward_id": "fork_nonos", "reward_name": "Nonos", "reward_rank": "B" },
                { "pool_group_id": "Arc_MiracleBox", "timestamp": "2026-07-01 09:00:00", "reward_type": "arc", "reward_id": "fork_nonos", "reward_name": "Nonos", "reward_rank": "B" }
              ]
            }
            """;

        var export = HottaGachaClient.ParseExport(json);

        Assert.Equal(3, export.Items.Count);
        Assert.Equal(3, export.Items.Select(x => x.Id).Distinct().Count());
        Assert.Equal(export.Items.OrderBy(x => x.Id).Select(x => x.Id), export.Items.Select(x => x.Id));
    }


    /// <summary>
    /// 導出檔只有英文名，界面是中文時要換成中文；別的語言維持英文
    /// </summary>
    [Theory]
    [InlineData("zh-TW", "娜娜莉")]
    [InlineData("zh-HK", "娜娜莉")]
    [InlineData("zh-CN", "娜娜莉")]
    [InlineData("en-US", "Nanally")]
    [InlineData("ja-JP", "Nanally")]
    public void Localize_SwitchesOnTheUiLanguage(string culture, string expected)
    {
        Assert.Equal(expected, HottaGachaNames.Localize("1010", "Nanally", new CultureInfo(culture)));
    }


    /// <summary>
    /// 简繁要分开：国服写「达芙蒂尔」，台服写「達芙蒂爾」
    /// </summary>
    [Fact]
    public void Localize_UsesSimplifiedForTheMainlandAndTraditionalElsewhere()
    {
        Assert.Equal("達芙蒂爾", HottaGachaNames.Localize("1054", "Daffodill", new CultureInfo("zh-TW")));
        Assert.Equal("达芙蒂尔", HottaGachaNames.Localize("1054", "Daffodill", new CultureInfo("zh-CN")));
    }


    /// <summary>
    /// 表里没有的一律退回导出文件里的名字，不猜
    /// </summary>
    [Theory]
    [InlineData("Dice_ticket_01", "Warp Piece")]
    [InlineData("Fashion_Glide_1072", "Sheepcopter")]
    [InlineData("", "whatever")]
    [InlineData(null, "whatever")]
    public void Localize_FallsBackWhenTheIdIsNotInTheTable(string? rewardId, string fallback)
    {
        Assert.Equal(fallback, HottaGachaNames.Localize(rewardId, fallback, new CultureInfo("zh-TW")));
    }


    /// <summary>
    /// 弧盤的名字也要換，而且解析出來的 RewardId 得原樣留著才查得到
    /// </summary>
    [Fact]
    public void ParseExport_KeepsTheRawRewardIdSoNamesCanBeLocalized()
    {
        var export = HottaGachaClient.ParseExport(ArcExport);

        HottaGachaItem item = Assert.Single(export.Items);
        Assert.Equal("fork_nonos", item.RewardId);
        Assert.Equal("成功的第一步", HottaGachaNames.Localize(item.RewardId, item.Name, new CultureInfo("zh-TW")));
    }


    /// <summary>
    /// 遊戲只記「有給東西」的那次投擲，一次十連在記錄裡是 11~13 條，
    /// 直接數條數會少算；單抽則只有 1 條。
    /// </summary>
    [Theory]
    [InlineData(13, 10)]
    [InlineData(11, 10)]
    [InlineData(6, 10)]
    [InlineData(3, 3)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void RollsInGroup_TreatsAFullRowGroupAsATenPull(int rowCount, int expected)
    {
        Assert.Equal(expected, HottaGachaType.RollsInGroup(rowCount));
    }


    /// <summary>
    /// 没有接口，所有取记录的入口都得明确失败，不能悄悄返回空
    /// </summary>
    [Fact]
    public async Task FetchingMembers_FailLoudlyBecauseThereIsNoApi()
    {
        var client = new HottaGachaClient();

        Assert.Null(client.FindGachaUrlFromLocalFiles(new GameBiz("hotta:nte:tw"), @"D:\Neverness To Everness"));
        Assert.Equal(0, await client.GetUidByGachaUrlAsync("whatever"));
        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetGachaLogAsync("whatever", cancellationToken: TestContext.Current.CancellationToken));
    }


}
