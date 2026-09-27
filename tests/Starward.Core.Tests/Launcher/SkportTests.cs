using Starward.Core.Launcher.Gryphline;
using System;
using System.Text.Json;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// SKPort 的签名与资料卡解析。
/// <para/>
/// 全部离线：JSON 是照真实响应的形状手写的，不碰真实接口。
/// </summary>
public class SkportTests
{

    /// <summary>
    /// 期望值由社区签到脚本（torikushiii/endfield-auto 的 generateSignV2）
    /// 在 Node 里对同样的输入算出来，不是照着本实现倒推的。
    /// 签名错一个字节接口就拒绝，这是整条链上最容易悄悄出错的地方。
    /// </summary>
    [Theory]
    [InlineData("/api/v1/game/endfield/card/detail", "7a2298b3f40030b75b9104e9520ee29c")]
    [InlineData("/api/v1/game/player/binding", "e56ec11a1c0120bc5bbea2340267568b")]
    public void SignV2_MatchesTheReferenceImplementation(string path, string expected)
    {
        string sign = SkportClient.SignV2(path, "", "1789000000", "test-salt-0123456789abcdef");

        Assert.Equal(expected, sign);
    }


    [Fact]
    public void GetGameRoleHeader_UsesThePlatformPrefix()
    {
        Assert.Equal("3_1234567890_2", SkportClient.GetGameRoleHeader("1234567890", "2"));
    }


    private const string BindingJson = """
        {"code":0,"message":"OK","data":{"list":[
          {"appCode":"arknights","bindingList":[{"defaultRole":{"roleId":"999","serverId":"1","nickname":"方舟","level":"120","serverName":"Asia"},"roles":[]}]},
          {"appCode":"endfield","bindingList":[
            {"defaultRole":{"roleId":"486286391","serverId":"2","nickname":"管理員","level":"42","serverName":"Asia"},
             "roles":[
               {"roleId":"486286391","serverId":"2","nickname":"管理員","level":"42","serverName":"Asia"},
               {"roleId":"111222333","serverId":"3","nickname":"副帳","level":"10","serverName":"Americas/Europe"}]}]}]}}
        """;


    /// <summary>
    /// 同一个 SKPort 账号可能同时绑了明日方舟，那些不能混进终末地的角色清单
    /// </summary>
    [Fact]
    public void Binding_KeepsOnlyEndfieldRolesAcrossServers()
    {
        var response = JsonSerializer.Deserialize<SkportResponse<SkportBindingData>>(BindingJson)!;

        var endfield = Assert.Single(response.Data!.List!, x => x.AppCode == SkportClient.ENDFIELD_APP_CODE);
        var roles = endfield.BindingList![0].Roles!;
        Assert.Equal(2, roles.Count);
        // 接口把等级写成字符串，也要读得出来
        Assert.Equal(42, roles[0].Level);
        Assert.Equal("3", roles[1].ServerId);
    }


    private const string CardJson = """
        {"code":0,"message":"OK","data":{"detail":{
          "base":{"roleId":"486286391","name":"管理員","serverName":"Asia","level":42,"worldLevel":5},
          "dungeon":{"curStamina":"180","maxStamina":"300","maxTs":"1789003600"},
          "dailyMission":{"dailyActivation":60,"maxDailyActivation":100},
          "bpSystem":{"curLevel":23,"maxLevel":60},
          "chars":[{"id":"chr_0001"}],"spaceShip":{"rooms":[]}}}}
        """;


    [Fact]
    public void CardDetail_ReadsStaminaDailyAndBattlePass()
    {
        var detail = JsonSerializer.Deserialize<SkportResponse<SkportCardDetailData>>(CardJson)!.Data!.Detail!;

        Assert.Equal("管理員", detail.Base!.Name);
        Assert.Equal(42, detail.Base.Level);
        Assert.Equal(180, detail.Dungeon!.CurStamina);
        Assert.Equal(300, detail.Dungeon.MaxStamina);
        Assert.Equal(60, detail.DailyMission!.DailyActivation);
        Assert.Equal(100, detail.DailyMission.MaxDailyActivation);
        Assert.Equal(23, detail.BattlePass!.CurLevel);
    }


    /// <summary>
    /// maxTs 的单位没有公开说明，秒与毫秒都得认
    /// </summary>
    [Fact]
    public void ToFullTime_AcceptsSecondsAndMilliseconds()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1789000000);
        var expected = DateTimeOffset.FromUnixTimeSeconds(1789003600);

        var seconds = new SkportCardDungeon { CurStamina = 180, MaxStamina = 300, MaxTs = 1789003600 };
        var millis = new SkportCardDungeon { CurStamina = 180, MaxStamina = 300, MaxTs = 1789003600_000 };

        Assert.Equal(expected, SkportCardMapper.ToFullTime(seconds, now));
        Assert.Equal(expected, SkportCardMapper.ToFullTime(millis, now));
    }


    /// <summary>
    /// 已经满了的时候接口可能给 0 或过去的时刻，都不该出现倒计时
    /// </summary>
    [Fact]
    public void ToFullTime_IsNullWhenAlreadyFull()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1789000000);

        Assert.Null(SkportCardMapper.ToFullTime(new SkportCardDungeon { CurStamina = 300, MaxStamina = 300, MaxTs = 1789003600 }, now));
        Assert.Null(SkportCardMapper.ToFullTime(new SkportCardDungeon { CurStamina = 180, MaxStamina = 300, MaxTs = 0 }, now));
        Assert.Null(SkportCardMapper.ToFullTime(new SkportCardDungeon { CurStamina = 180, MaxStamina = 300, MaxTs = 1788990000 }, now));
        Assert.Null(SkportCardMapper.ToFullTime(null, now));
    }

}
