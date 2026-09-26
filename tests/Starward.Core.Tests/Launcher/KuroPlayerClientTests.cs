using Starward.Core.Launcher.Kuro;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 鸣潮启动器角色卡片的本机凭证与查询接口。
/// <para/>
/// 全部离线：凭证与角色数据都是手写的假值，接口由假的 HttpMessageHandler 回应。
/// 回应的形状照抄官方启动器日志里的真实回应，只换了数值。
/// </summary>
public class KuroPlayerClientTests
{

    private const string FakeOauthCode = "11111111-2222-3333-4444-555555555555";


    [Fact]
    public void Deobfuscate_XorsEveryCharWithFive()
    {
        // 官方日志里同一个凭证的两种样子：缓存文件里的与发给接口的
        Assert.Equal("cfae7213-4ca5", KuroSdkAccount.Deobfuscate("fcd`2746(1fd0"));
        // 异或是自反的
        Assert.Equal(FakeOauthCode, KuroSdkAccount.Deobfuscate(KuroSdkAccount.Deobfuscate(FakeOauthCode)));
    }


    [Fact]
    public void Parse_RestoresTheCodeAndDropsRecordsWithoutOne()
    {
        string json = $$"""
            [
              {"cuid":"100000001","email":"a@example.com","id":100000001.0,"loginType":18,"oauthCode":"{{KuroSdkAccount.Deobfuscate(FakeOauthCode)}}","thirdNickName":"Nick","username":"U100000001A"},
              {"cuid":"100000002","email":"","id":100000002.0,"loginType":18,"oauthCode":"","thirdNickName":"","username":"U100000002A"}
            ]
            """;

        var accounts = KuroSdkAccount.Parse(json);

        var account = Assert.Single(accounts);
        Assert.Equal("100000001", account.Cuid);
        Assert.Equal(FakeOauthCode, account.OauthCode);
        Assert.Equal("Nick", account.DisplayName);
    }


    [Fact]
    public void ReadAll_ScansEverySdkFolderAndDeduplicatesAccounts()
    {
        string root = Path.Combine(Path.GetTempPath(), $"starward-kuro-{Guid.NewGuid():N}");
        try
        {
            string code = KuroSdkAccount.Deobfuscate(FakeOauthCode);
            Directory.CreateDirectory(Path.Combine(root, "A1730"));
            Directory.CreateDirectory(Path.Combine(root, "A9999"));
            Directory.CreateDirectory(Path.Combine(root, "Images"));
            File.WriteAllText(Path.Combine(root, "A1730", "KRSDKUserLauncherCache.json"), $$"""[{"cuid":"1","oauthCode":"{{code}}","username":"U1A"}]""");
            File.WriteAllText(Path.Combine(root, "A9999", "KRSDKUserLauncherCache.json"), $$"""[{"cuid":"1","oauthCode":"{{code}}"},{"cuid":"2","oauthCode":"{{code}}","email":"b@example.com"}]""");
            // 损坏的文件不能拖累其他目录
            Directory.CreateDirectory(Path.Combine(root, "Broken"));
            File.WriteAllText(Path.Combine(root, "Broken", "KRSDKUserLauncherCache.json"), "not json");

            var accounts = KuroSdkAccount.ReadAll(root);

            Assert.Equal(["1", "2"], accounts.Select(x => x.Cuid).Order());
            Assert.Equal("b@example.com", accounts.Single(x => x.Cuid == "2").DisplayName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }


    [Fact]
    public void ReadAll_ReturnsEmptyWhenTheGameWasNeverLoggedIn()
    {
        Assert.Empty(KuroSdkAccount.ReadAll(Path.Combine(Path.GetTempPath(), $"starward-missing-{Guid.NewGuid():N}")));
    }


    [Fact]
    public async Task GetPlayersAsync_DecodesTheNestedJsonPerRegion()
    {
        // data 的值是再编码过一次的 JSON 字符串
        string body = Envelope(0, new()
        {
            ["HMT"] = """{"roleId":"800000001","roleName":"Rover","level":80,"sex":0,"headPhoto":82001211}""",
            ["Asia"] = """{"roleId":"700000002","roleName":"Alt","level":9,"sex":0,"headPhoto":82001502}""",
        });
        var handler = new StubHandler(body);
        var client = new KuroPlayerClient(new HttpClient(handler), TimeSpan.Zero);

        var players = await client.GetPlayersAsync(FakeOauthCode, TestContext.Current.CancellationToken);

        Assert.Equal(2, players.Count);
        var hmt = players.Single(x => x.Region == "HMT");
        Assert.Equal("800000001", hmt.RoleId);
        Assert.Equal("Rover", hmt.RoleName);
        Assert.Equal(80, hmt.Level);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/game/queryPlayerInfo", request.Path);
        Assert.Equal(FakeOauthCode, JsonDocument.Parse(request.Body).RootElement.GetProperty("oauthCode").GetString());
    }


    [Fact]
    public async Task GetRoleAsync_RetriesWhileTheServerCacheIsEmpty()
    {
        string role = """
            {"Base":{"Name":"Rover","Id":800000001,"Level":80,"Energy":5,"MaxEnergy":240,"StoreEnergy":396,"MaxStoreEnergy":480,"EnergyRecoverTime":1790521840202,"Liveness":100,"LivenessMaxCount":100,"LivenessUnlock":true},
             "BattlePass":{"Level":43,"WeekExp":7500,"WeekMaxExp":12000,"IsUnlock":true,"IsOpen":true,"Exp":200,"ExpLimit":1000},
             "MotorData":{"Level":20}}
            """;
        var handler = new StubHandler(Envelope(1005, null), Envelope(1005, null), Envelope(0, new() { ["HMT"] = role }));
        var client = new KuroPlayerClient(new HttpClient(handler), TimeSpan.Zero);

        var data = await client.GetRoleAsync(FakeOauthCode, "800000001", "HMT", TestContext.Current.CancellationToken);

        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, x => Assert.Equal("/game/queryRole", x.Path));
        var body = JsonDocument.Parse(handler.Requests[0].Body).RootElement;
        Assert.Equal("800000001", body.GetProperty("playerId").GetString());
        Assert.Equal("HMT", body.GetProperty("region").GetString());
        Assert.Equal(5, data.Base!.Energy);
        Assert.Equal(240, data.Base.MaxEnergy);
        Assert.Equal(1790521840202, data.Base.EnergyRecoverTime);
        Assert.Equal(396, data.Base.StoreEnergy);
        Assert.Equal(43, data.BattlePass!.Level);
        Assert.Equal(7500, data.BattlePass.WeekExp);
    }


    [Fact]
    public async Task GetRoleAsync_GivesUpAfterFourRetries()
    {
        var handler = new StubHandler(Enumerable.Repeat(Envelope(1005, null), 10).ToArray());
        var client = new KuroPlayerClient(new HttpClient(handler), TimeSpan.Zero);

        var ex = await Assert.ThrowsAsync<KuroPlayerApiException>(() => client.GetRoleAsync(FakeOauthCode, "1", "HMT", TestContext.Current.CancellationToken));

        Assert.Equal(KuroPlayerResultCode.RedisEmpty, ex.Code);
        // 第一次加上四次重试
        Assert.Equal(5, handler.Requests.Count);
    }


    [Fact]
    public async Task GetPlayersAsync_ThrowsTheCodeWhenTheLoginExpired()
    {
        var client = new KuroPlayerClient(new HttpClient(new StubHandler(Envelope(1001, null))), TimeSpan.Zero);

        var ex = await Assert.ThrowsAsync<KuroPlayerApiException>(() => client.GetPlayersAsync(FakeOauthCode, TestContext.Current.CancellationToken));

        Assert.Equal(KuroPlayerResultCode.LoginExpired, ex.Code);
    }


    [Fact]
    public async Task GetPlayersAsync_ReturnsEmptyWhenTheAccountHasNoCharacter()
    {
        var client = new KuroPlayerClient(new HttpClient(new StubHandler(Envelope(1003, null))), TimeSpan.Zero);

        Assert.Empty(await client.GetPlayersAsync(FakeOauthCode, TestContext.Current.CancellationToken));
    }



    private static string Envelope(int code, Dictionary<string, string>? data)
    {
        return JsonSerializer.Serialize(new { code, message = code == 0 ? "success" : "error", data, timestamp = 1790437486451 });
    }


    private sealed class StubHandler(params string[] responses) : HttpMessageHandler
    {
        private int _index;

        public List<(string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsolutePath, body));
            string response = responses[Math.Min(_index++, responses.Length - 1)];
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

}
