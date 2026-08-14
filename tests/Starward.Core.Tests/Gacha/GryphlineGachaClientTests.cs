using Starward.Core.Gacha.Gryphline;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Starward.Core.Tests.Gacha;

/// <summary>
/// 终末地的寻访记录协议。
/// <para/>
/// 全部离线：URL 里的 token 是假值，记录也是手写的，不碰真实接口。
/// </summary>
public class GryphlineGachaClientTests
{

    private const string CharacterRecordUrl = "https://ef-webview.gryphline.com/api/record/char?lang=zh-tw&token=fake_token&server_id=1";

    private const string WeaponRecordUrl = "https://ef-webview.hypergryph.com/api/record/weapon?lang=zh-cn&token=fake_token&server_id=2";


    [Fact]
    public void FindRecordUrl_TakesTheNewestUsableUrl()
    {
        // 缓存里既有能用的，也有缺 token 的残留
        string cache = $"\0{CharacterRecordUrl}\0garbage\0https://ef-webview.gryphline.com/api/record/char?lang=zh-tw&server_id=1\0";

        string? found = GryphlineGachaClient.FindRecordUrl(cache);

        Assert.Equal(CharacterRecordUrl, found);
    }


    [Fact]
    public void FindRecordUrl_ReturnsNullWhenNothingMatches()
    {
        Assert.Null(GryphlineGachaClient.FindRecordUrl("no record url here"));
    }


    [Fact]
    public void ParseRecordUrl_ReadsTheAuthorisationAndFillsDefaults()
    {
        var auth = GryphlineGachaClient.ParseRecordUrl(CharacterRecordUrl);
        Assert.Equal("ef-webview.gryphline.com", auth.Host);
        Assert.Equal("fake_token", auth.Token);
        Assert.Equal("1", auth.ServerId);
        Assert.Equal("zh-tw", auth.Language);

        // 缺省的区服与语言有默认值
        var minimal = GryphlineGachaClient.ParseRecordUrl("https://ef-webview.gryphline.com/api/record/char?token=fake_token");
        Assert.Equal("1", minimal.ServerId);
        Assert.Equal("zh-tw", minimal.Language);
    }


    [Theory]
    // 主机不在名单里
    [InlineData("https://example.com/api/record/char?token=fake_token")]
    // 不是寻访记录接口
    [InlineData("https://ef-webview.gryphline.com/api/other?token=fake_token")]
    // 没有 token
    [InlineData("https://ef-webview.gryphline.com/api/record/char?lang=zh-tw")]
    // 不是 https
    [InlineData("http://ef-webview.gryphline.com/api/record/char?token=fake_token")]
    public void ParseRecordUrl_RejectsUrlsItCannotTrust(string url)
    {
        Assert.Throws<ArgumentException>(() => GryphlineGachaClient.ParseRecordUrl(url));
        Assert.False(GryphlineGachaClient.TryParseRecordUrl(url, out _));
    }


    [Fact]
    public async Task GetUidByGachaUrlAsync_UsesTheServerId()
    {
        var client = new GryphlineGachaClient();

        // 寻访 URL 里没有玩家 ID，只有区服
        Assert.Equal(1, await client.GetUidByGachaUrlAsync(CharacterRecordUrl));
        Assert.Equal(2, await client.GetUidByGachaUrlAsync(WeaponRecordUrl));
    }


    [Fact]
    public void GachaType_MapsToThePoolParameterTheApiExpects()
    {
        Assert.Equal("E_CharacterGachaPoolType_Special", new GryphlineGachaType(GryphlineGachaType.Special).ToPoolTypeParameter());
        Assert.Equal("E_CharacterGachaPoolType_Joint", new GryphlineGachaType(GryphlineGachaType.Joint).ToPoolTypeParameter());
        Assert.Equal("E_CharacterGachaPoolType_Standard", new GryphlineGachaType(GryphlineGachaType.Standard).ToPoolTypeParameter());
        Assert.Equal("E_CharacterGachaPoolType_Beginner", new GryphlineGachaType(GryphlineGachaType.Beginner).ToPoolTypeParameter());
        // 武器走另一个接口，不带 pool_type
        Assert.Null(new GryphlineGachaType(GryphlineGachaType.Weapon).ToPoolTypeParameter());
        Assert.True(new GryphlineGachaType(GryphlineGachaType.Weapon).IsWeapon);
    }


    [Fact]
    public void ToGachaLogItems_MapsCharacterRecords()
    {
        var auth = GryphlineGachaClient.ParseRecordUrl(CharacterRecordUrl);
        var records = new List<GryphlineGachaRecord>
        {
            new()
            {
                SeqId = "1024",
                CharId = "char_1001_endmi",
                CharName = "恩德米",
                Rarity = 6,
                GachaTs = 1785000000000,
                PoolId = "pool_special_01",
                PoolName = "特许寻访",
                IsNew = true,
            },
        };

        var items = GryphlineGachaClient.ToGachaLogItems(auth, GryphlineGachaType.Special, records);

        var item = Assert.Single(items);
        Assert.Equal(1, item.Uid);
        Assert.Equal(GryphlineGachaType.Special, item.GachaType);
        Assert.Equal("恩德米", item.Name);
        Assert.Equal("Character", item.ItemType);
        // 终末地最高 6★，按游戏本身的稀有度存
        Assert.Equal(6, item.RankType);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1785000000000).LocalDateTime, item.Time);
        // 物品 ID 是字符串，散列成整数后同一角色始终得到同一个值
        Assert.NotEqual(0, item.ItemId);
        Assert.Equal(item.ItemId, GryphlineGachaClient.ToGachaLogItems(auth, GryphlineGachaType.Special, records)[0].ItemId);
        Assert.Equal("zh-tw", item.Lang);
        Assert.IsType<GryphlineGachaType>(item.GetGachaType());
    }


    [Fact]
    public void ToGachaLogItems_KeepsCharacterAndWeaponRecordsApart()
    {
        var auth = GryphlineGachaClient.ParseRecordUrl(CharacterRecordUrl);
        var character = new List<GryphlineGachaRecord>
        {
            new() { SeqId = "1024", CharId = "char_1001_endmi", CharName = "恩德米", Rarity = 6, GachaTs = 1785000000000 },
        };
        var weapon = new List<GryphlineGachaRecord>
        {
            new() { SeqId = "1024", WeaponId = "weapon_2001", WeaponName = "测试武器", WeaponType = "Sword", Rarity = 5, GachaTs = 1785000001000 },
        };

        var characterItems = GryphlineGachaClient.ToGachaLogItems(auth, GryphlineGachaType.Special, character);
        var weaponItems = GryphlineGachaClient.ToGachaLogItems(auth, GryphlineGachaType.Weapon, weapon);

        // 两套接口的 seqId 各自编号，同号不能撞成同一条记录
        Assert.NotEqual(characterItems[0].Id, weaponItems[0].Id);
        Assert.Equal("Weapon", weaponItems[0].ItemType);
        Assert.Equal("测试武器", weaponItems[0].Name);
    }


    [Fact]
    public void ToGachaLogItems_KeepsSeqIdOrderAndSkipsRecordsWithoutIt()
    {
        var auth = GryphlineGachaClient.ParseRecordUrl(CharacterRecordUrl);
        var records = new List<GryphlineGachaRecord>
        {
            new() { SeqId = "10", CharName = "A", Rarity = 4, GachaTs = 1785000000000 },
            new() { SeqId = "11", CharName = "B", Rarity = 5, GachaTs = 1785000001000 },
            new() { SeqId = "", CharName = "无效", Rarity = 4, GachaTs = 1785000002000 },
        };

        var items = GryphlineGachaClient.ToGachaLogItems(auth, GryphlineGachaType.Standard, records);

        Assert.Equal(2, items.Count);
        Assert.True(items[0].Id < items[1].Id);
    }


    [Fact]
    public void QueryGachaTypes_CoverTheFourCharacterPoolsAndTheWeaponPool()
    {
        var client = new GryphlineGachaClient();

        Assert.Equal(5, client.QueryGachaTypes.Count);
        Assert.All(client.QueryGachaTypes, x => Assert.False(string.IsNullOrEmpty(x.ToLocalization())));
    }

}
