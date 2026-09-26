using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Starward.Core.Launcher.Kuro;

/// <summary>
/// 鸣潮官方启动器首页「角色卡片」用的接口：联觉等级、结晶波片、活跃度、先约电台。
/// <para/>
/// 取自官方启动器网页前端（<c>krfeapp.dat</c> 里的 <c>gameApiBase</c>）与它的日志。
/// 两支接口都是 POST + JSON，凭证是游戏 SDK 留在本机的 <see cref="KuroSdkAccount.OauthCode"/>，
/// 不需要库街区账号，也不需要再登录一次。
/// <para/>
/// 数据来自服务器端缓存，第一次问常会得到 <see cref="KuroPlayerResultCode.RedisEmpty"/>，
/// 官方的做法是隔一秒再问，最多重试四次，这里照做。
/// </summary>
public class KuroPlayerClient
{


    /// <summary>
    /// 国际服。国服的启动器是另一份前端，本项目暂时只支持国际服。
    /// </summary>
    private const string API_BASE = "https://pc-launcher-sdk-api.kurogame.net";


    private const int MAX_RETRY_COUNT = 4;


    private readonly HttpClient _httpClient;

    private readonly TimeSpan _retryDelay;


    public KuroPlayerClient(HttpClient? httpClient = null) : this(httpClient, TimeSpan.FromSeconds(1))
    {

    }


    /// <param name="retryDelay">遇到服务器缓存未就绪时的重试间隔，测试时可以设为零</param>
    public KuroPlayerClient(HttpClient? httpClient, TimeSpan retryDelay)
    {
        _httpClient = httpClient ?? new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
        _retryDelay = retryDelay;
    }



    /// <summary>
    /// 这个账号在各服务器区域上的角色，没有角色时返回空列表
    /// </summary>
    public async Task<List<KuroPlayerSummary>> GetPlayersAsync(string oauthCode, CancellationToken cancellationToken = default)
    {
        var request = new Dictionary<string, string> { ["oauthCode"] = oauthCode };
        Dictionary<string, string> data;
        try
        {
            data = await PostAsync("/game/queryPlayerInfo", request, cancellationToken);
        }
        catch (KuroPlayerApiException ex) when (ex.Code is KuroPlayerResultCode.NoCharacter)
        {
            return [];
        }
        var list = new List<KuroPlayerSummary>();
        foreach ((string region, string json) in data)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }
            if (JsonSerializer.Deserialize(json, KuroPlayerJsonContext.Default.KuroPlayerSummary) is KuroPlayerSummary summary)
            {
                summary.Region = region;
                list.Add(summary);
            }
        }
        return list;
    }



    /// <summary>
    /// 角色的详细数据
    /// </summary>
    /// <param name="region">服务器区域，取自 <see cref="KuroPlayerSummary.Region"/></param>
    public async Task<KuroRoleData> GetRoleAsync(string oauthCode, string roleId, string region, CancellationToken cancellationToken = default)
    {
        var request = new Dictionary<string, string>
        {
            ["oauthCode"] = oauthCode,
            ["playerId"] = roleId,
            ["region"] = region,
        };
        Dictionary<string, string> data = await PostAsync("/game/queryRole", request, cancellationToken);
        if (!data.TryGetValue(region, out string? json) || string.IsNullOrWhiteSpace(json))
        {
            throw new KuroPlayerApiException(KuroPlayerResultCode.CharacterNotFound, "Role data is empty");
        }
        return JsonSerializer.Deserialize(json, KuroPlayerJsonContext.Default.KuroRoleData)
            ?? throw new KuroPlayerApiException(KuroPlayerResultCode.CharacterNotFound, "Role data is empty");
    }



    private async Task<Dictionary<string, string>> PostAsync(string path, Dictionary<string, string> body, CancellationToken cancellationToken)
    {
        for (int retry = 0; ; retry++)
        {
            // 官方每次请求都带秒级时间戳，看起来只是防缓存，照抄
            string url = $"{API_BASE}{path}?_t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            using var response = await _httpClient.PostAsJsonAsync(url, body, KuroPlayerJsonContext.Default.DictionaryStringString, cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync(KuroPlayerJsonContext.Default.KuroPlayerResponse, cancellationToken)
                ?? throw new KuroPlayerApiException(-1, "Empty response");
            if (result.Code is KuroPlayerResultCode.RedisEmpty && retry < MAX_RETRY_COUNT)
            {
                await Task.Delay(_retryDelay, cancellationToken);
                continue;
            }
            if (result.Code is not KuroPlayerResultCode.Success)
            {
                throw new KuroPlayerApiException(result.Code, result.Message);
            }
            return result.Data ?? [];
        }
    }


}
