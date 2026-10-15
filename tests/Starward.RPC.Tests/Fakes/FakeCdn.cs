using System.Collections.Concurrent;
using System.Net;

namespace Starward.RPC.Tests.Fakes;

/// <summary>
/// 放在记忆体里的 CDN：登记过的网址回传内容，其余一律 404，并记下每一个请求。
/// 取配置的 CDN 与下载的 CDN 都由它回答，所以测试完全不碰网络。
/// </summary>
internal sealed class FakeCdn : HttpMessageHandler, IHttpClientFactory
{

    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);


    /// <summary>
    /// 收到的请求，网址已经还原转义
    /// </summary>
    public ConcurrentQueue<string> Requests { get; } = new();


    public void Add(string url, byte[] content) => _files[url] = content;


    public void Remove(string url) => _files.TryRemove(url, out _);


    public bool WasRequested(string fragment) => Requests.Any(x => x.Contains(fragment, StringComparison.Ordinal));


    public int CountRequests(string fragment) => Requests.Count(x => x.Contains(fragment, StringComparison.Ordinal));


    /// <summary>
    /// 每个请求进来时调用，用来在下载途中模拟中断
    /// </summary>
    public Action<string>? OnRequest { get; set; }


    /// <summary>
    /// 按请求正文回答的网址（例如同一个接口靠 POST 的内容区分），参数是网址与正文，返回 null 时照常查登记的内容
    /// </summary>
    public Func<string, string, byte[]?>? Responder { get; set; }


    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = Uri.UnescapeDataString(request.RequestUri!.AbsoluteUri);
        Requests.Enqueue(url);
        OnRequest?.Invoke(url);
        cancellationToken.ThrowIfCancellationRequested();
        if (Responder is not null)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (Responder(url, body) is byte[] response)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(response) };
            }
        }
        if (_files.TryGetValue(url, out byte[]? content))
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }


    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

}
