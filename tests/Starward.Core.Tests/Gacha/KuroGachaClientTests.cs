using Starward.Core.Gacha.Kuro;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace Starward.Core.Tests.Gacha;

/// <summary>
/// 鸣潮的唤取记录协议。
/// <para/>
/// 全部离线：URL 里的 token 是假值，记录也是手写的，不碰真实接口。
/// </summary>
public class KuroGachaClientTests
{

    /// <summary>
    /// 形状与游戏日志里的一致，参数值全是假的
    /// </summary>
    private const string GlobalConveneUrl = "https://aki-gm-resources-oversea.aki-game.net/aki/gacha/index.html#/record?svr_id=fake_server&player_id=100000001&lang=zh-Hant&gacha_id=1&gacha_type=1&svr_area=global&record_id=fake_record&resources_id=fake_resources";

    private const string ChinaConveneUrl = "https://aki-gm-resources.aki-game.com/aki/gacha/index.html#/record?svr_id=fake_server&player_id=100000002&lang=zh-Hans&record_id=fake_record&resources_id=fake_resources";


    [Fact]
    public void FindConveneUrl_TakesTheLastUrlInTheLog()
    {
        string log = $"noise\n{ChinaConveneUrl}\nmore noise\n{GlobalConveneUrl}\"trailing";

        string? found = KuroGachaClient.FindConveneUrl(log);

        Assert.Equal(GlobalConveneUrl, found);
    }


    [Fact]
    public void FindConveneUrl_ReturnsNullWhenTheLogHasNoUrl()
    {
        Assert.Null(KuroGachaClient.FindConveneUrl("nothing to see here"));
    }


    [Fact]
    public void DecodeClientLog_RoundTripsTheObfuscation()
    {
        byte[] plain = Encoding.UTF8.GetBytes($"prefix {GlobalConveneUrl} suffix");
        // 两个密钥都是奇数，异或必定翻转最低位，因此密文的奇偶与明文相反：
        // 解码看密文的奇偶，编码就得看明文的奇偶，两边不能写成同一个判断
        byte[] obfuscated = plain.Select(x => (byte)(x ^ ((x & 1) == 0 ? 0xA5 : 0xEF))).ToArray();

        string decoded = KuroGachaClient.DecodeClientLog(obfuscated);

        Assert.Equal(GlobalConveneUrl, KuroGachaClient.FindConveneUrl(decoded));
    }


    [Fact]
    public void ParseConveneUrl_ReadsTheAccountAndPicksTheServiceByHost()
    {
        var global = KuroGachaClient.ParseConveneUrl(GlobalConveneUrl);
        Assert.Equal("100000001", global.PlayerId);
        Assert.Equal("fake_server", global.ServerId);
        Assert.Equal("fake_resources", global.CardPoolId);
        Assert.Equal("fake_record", global.RecordId);
        Assert.Equal("zh-Hant", global.LanguageCode);
        Assert.Equal("https://gmserver-api.aki-game2.net/gacha/record/query", global.RecordApiUrl);

        var china = KuroGachaClient.ParseConveneUrl(ChinaConveneUrl);
        Assert.Equal("https://gmserver-api.aki-game2.com/gacha/record/query", china.RecordApiUrl);
    }


    [Theory]
    // 主机不在名单里
    [InlineData("https://example.com/aki/gacha/index.html#/record?player_id=1&svr_id=a&record_id=b&resources_id=c")]
    // 不是唤取页面
    [InlineData("https://aki-gm-resources.aki-game.com/aki/other.html#/record?player_id=1&svr_id=a&record_id=b&resources_id=c")]
    // 缺少必要参数
    [InlineData("https://aki-gm-resources.aki-game.com/aki/gacha/index.html#/record?player_id=1")]
    // 没有查询串
    [InlineData("https://aki-gm-resources.aki-game.com/aki/gacha/index.html#/record")]
    // 不是 https
    [InlineData("http://aki-gm-resources.aki-game.com/aki/gacha/index.html#/record?player_id=1&svr_id=a&record_id=b&resources_id=c")]
    public void ParseConveneUrl_RejectsUrlsItCannotTrust(string url)
    {
        Assert.Throws<ArgumentException>(() => KuroGachaClient.ParseConveneUrl(url));
    }


    [Fact]
    public async Task GetUidByGachaUrlAsync_ReadsThePlayerIdFromTheUrl()
    {
        var client = new KuroGachaClient();

        long uid = await client.GetUidByGachaUrlAsync(GlobalConveneUrl);

        Assert.Equal(100000001, uid);
    }


    [Fact]
    public void ToGachaLogItems_MapsEveryField()
    {
        var auth = KuroGachaClient.ParseConveneUrl(GlobalConveneUrl);
        var records = new List<KuroGachaRecord>
        {
            new() { ResourceId = 1304, QualityLevel = 5, ResourceType = "角色", Name = "今汐", Count = 1, Time = "2026-08-01 20:30:00" },
        };

        var items = KuroGachaClient.ToGachaLogItems(auth, KuroGachaType.FeaturedResonator, records);

        var item = Assert.Single(items);
        Assert.Equal(100000001, item.Uid);
        Assert.Equal(KuroGachaType.FeaturedResonator, item.GachaType);
        Assert.Equal("今汐", item.Name);
        Assert.Equal("角色", item.ItemType);
        Assert.Equal(5, item.RankType);
        Assert.Equal(new DateTime(2026, 8, 1, 20, 30, 0), item.Time);
        Assert.Equal(1304, item.ItemId);
        Assert.Equal(1, item.Count);
        Assert.Equal("zh-Hant", item.Lang);
        Assert.IsType<KuroGachaType>(item.GetGachaType());
    }


    [Fact]
    public void ToGachaLogItems_SynthesisesIdsThatSortByTimeAndSurviveARefetch()
    {
        var auth = KuroGachaClient.ParseConveneUrl(GlobalConveneUrl);
        // 接口由新到旧返回，同一秒内的十连也在同一页
        var records = new List<KuroGachaRecord>
        {
            new() { ResourceId = 1105, QualityLevel = 4, Name = "秧秧", Time = "2026-08-02 09:00:00" },
            new() { ResourceId = 1304, QualityLevel = 5, Name = "今汐", Time = "2026-08-01 20:30:00" },
            new() { ResourceId = 1203, QualityLevel = 4, Name = "凌阳", Time = "2026-08-01 20:30:00" },
            new() { ResourceId = 1102, QualityLevel = 3, Name = "白芷", Time = "2026-08-01 20:30:00" },
        };

        var items = KuroGachaClient.ToGachaLogItems(auth, KuroGachaType.FeaturedResonator, records);

        // 时间升序，ID 也随之升序，界面与保底都依赖这个顺序
        Assert.Equal(items.OrderBy(x => x.Time).Select(x => x.Id), items.Select(x => x.Id));
        Assert.Equal(items.Select(x => x.Id).Distinct().Count(), items.Count);

        // 再抓一次得到同样的 ID，INSERT OR REPLACE 才不会插出重复记录
        var again = KuroGachaClient.ToGachaLogItems(auth, KuroGachaType.FeaturedResonator, records);
        Assert.Equal(items.Select(x => x.Id), again.Select(x => x.Id));

        // 换一个卡池就是另一条记录
        var otherPool = KuroGachaClient.ToGachaLogItems(auth, KuroGachaType.StandardResonator, records);
        Assert.Empty(otherPool.Select(x => x.Id).Intersect(items.Select(x => x.Id)));
    }


    [Fact]
    public void QueryGachaTypes_CoverTheSevenPools()
    {
        var client = new KuroGachaClient();

        Assert.Equal(7, client.QueryGachaTypes.Count);
        Assert.All(client.QueryGachaTypes, x => Assert.False(string.IsNullOrEmpty(x.ToLocalization())));
    }


    [Fact]
    public void GetLogFileCandidates_ReturnsNothingWithoutAnInstallPath()
    {
        Assert.Empty(KuroGachaClient.GetLogFileCandidates(null));
        Assert.Empty(KuroGachaClient.GetLogFileCandidates("  "));
    }

}
