using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZstdSharp;

namespace Starward.Core.Tests;

/// <summary>
/// 下载的读取超时。
/// <para/>
/// 本机的 TcpListener 当 CDN：送完响应头与一部分正文之后既不关连接也不再送数据，
/// 跟网络断过一下、收不到 RST 的连接一样，客户端的 ReadAsync 永远等不到下一个包。
/// 超时设成 1 秒，正式代码用的是 <see cref="IdleTimeoutStream.DefaultTimeout"/>。
/// 有问题的实现会一直挂着，所以每个测试都有上限。
/// </summary>
public class IdleTimeoutStreamTests
{

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(1);

    private const int TestTimeout = 30_000;


    private static byte[] CreatePayload(int length)
    {
        byte[] bytes = new byte[length];
        new Random(42).NextBytes(bytes);
        return bytes;
    }



    [Fact(Timeout = TestTimeout)]
    public async Task StalledConnection_ThrowsTimeoutException()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] payload = CreatePayload(1 << 20);
        await using var server = new StallingHttpServer(payload, stallAt: _ => payload.Length / 2);
        using var client = new HttpClient();

        using HttpResponseMessage response = await client.GetAsync(server.Url, HttpCompletionOption.ResponseHeadersRead, token);
        using var stream = new IdleTimeoutStream(await response.Content.ReadAsStreamAsync(token), IdleTimeout);
        byte[] buffer = new byte[8192];
        long received = 0;
        var sinceLastByte = Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0)
            {
                received += read;
                sinceLastByte.Restart();
            }
        });

        // 断掉之前送出的都收到了，最后一个字节之后大约一个超时才抛出
        Assert.Equal(payload.Length / 2, received);
        Assert.InRange(sinceLastByte.Elapsed, IdleTimeout * 0.9, IdleTimeout * 5);
    }



    [Fact(Timeout = TestTimeout)]
    public async Task CallerCancel_ThrowsOperationCanceledExceptionWithTheCallersToken()
    {
        byte[] payload = CreatePayload(64 << 10);
        await using var server = new StallingHttpServer(payload, stallAt: _ => 1000);
        using var client = new HttpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        using HttpResponseMessage response = await client.GetAsync(server.Url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        // 超时比取消晚得多，抛出来的只能是取消
        using var stream = new IdleTimeoutStream(await response.Content.ReadAsStreamAsync(cts.Token), TimeSpan.FromSeconds(20));
        byte[] buffer = new byte[8192];
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));
        var stopwatch = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            while (await stream.ReadAsync(buffer, cts.Token) > 0) { }
        });

        Assert.Equal(cts.Token, ex.CancellationToken);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Cancel took {stopwatch.Elapsed}.");
    }



    [Fact(Timeout = TestTimeout)]
    public async Task SlowButSteadyConnection_DoesNotTimeOut()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        // 每 0.3 秒才来 16 KB，整个下载超过 2 秒，但每次等待都不到超时
        byte[] payload = CreatePayload(8 * StallingHttpServer.ChunkSize);
        await using var server = new StallingHttpServer(payload, chunkDelay: TimeSpan.FromMilliseconds(300));
        using var client = new HttpClient();

        using HttpResponseMessage response = await client.GetAsync(server.Url, HttpCompletionOption.ResponseHeadersRead, token);
        using var stream = new IdleTimeoutStream(await response.Content.ReadAsStreamAsync(token), IdleTimeout);
        using var output = new MemoryStream();
        var stopwatch = Stopwatch.StartNew();
        await stream.CopyToAsync(output, token);

        Assert.True(stopwatch.Elapsed > IdleTimeout * 2, $"Download took only {stopwatch.Elapsed}.");
        Assert.Equal(payload, output.ToArray());
    }



    [Fact(Timeout = TestTimeout)]
    public async Task TimeSpentBetweenReads_IsNotCounted()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] payload = CreatePayload(64 << 10);
        await using var server = new StallingHttpServer(payload);
        using var client = new HttpClient();

        using HttpResponseMessage response = await client.GetAsync(server.Url, HttpCompletionOption.ResponseHeadersRead, token);
        using var stream = new IdleTimeoutStream(await response.Content.ReadAsStreamAsync(token), IdleTimeout);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            output.Write(buffer, 0, read);
            if (output.Length == read)
            {
                // 限速排队、写文件花的时间比超时还长
                await Task.Delay(IdleTimeout * 2, token);
            }
        }

        Assert.Equal(payload, output.ToArray());
    }



    [Fact(Timeout = TestTimeout)]
    public async Task StallThenRetry_ResumesFromTheWrittenLengthAndMatchesTheChecksum()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] payload = CreatePayload(3 << 20);
        int stallAt = payload.Length * 2 / 5;
        // 第一次请求送到 40% 就不动了，之后的请求都正常
        await using var server = new StallingHttpServer(payload, stallAt: index => index == 0 ? stallAt : null);
        using var client = new HttpClient();
        using var file = new MemoryStream();

        int timeouts = 0;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await DownloadRemainingAsync(client, server.Url, file, token);
                break;
            }
            catch (TimeoutException)
            {
                timeouts++;
            }
        }

        Assert.Equal(1, timeouts);
        // 重试从已经写入的长度开始要，不是从头
        Assert.Equal(new long?[] { 0, stallAt }, server.RangeStarts);
        Assert.Equal(SHA256.HashData(payload), SHA256.HashData(file.ToArray()));
    }


    /// <summary>
    /// 与正式代码相同的续传写法：从已经写入的长度开始要，接着写到后面
    /// </summary>
    private static async Task DownloadRemainingAsync(HttpClient client, Uri url, Stream file, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(file.Length, null);
        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        file.Position = response.Content.Headers.ContentRange?.From ?? 0;
        file.SetLength(file.Position);
        using var hs = new IdleTimeoutStream(await response.Content.ReadAsStreamAsync(cancellationToken), IdleTimeout);
        byte[] buffer = new byte[1 << 16];
        int read;
        while ((read = await hs.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }



    [Fact(Timeout = TestTimeout)]
    public async Task DecompressingCopy_TimesOutToo()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        // 解压流从内层读的是 byte[] 的 ReadAsync 重载，CopyToAsync 走基类的实现，都要经过计时
        using var compressor = new Compressor();
        byte[] compressed = compressor.Wrap(CreatePayload(1 << 20)).ToArray();
        await using var server = new StallingHttpServer(compressed, stallAt: _ => compressed.Length / 2);
        using var client = new HttpClient();

        using HttpResponseMessage response = await client.GetAsync(server.Url, HttpCompletionOption.ResponseHeadersRead, token);
        using var hs = new IdleTimeoutStream(await response.Content.ReadAsStreamAsync(token), IdleTimeout);
        using var zstd = new DecompressionStream(hs);
        using var output = new MemoryStream();

        await Assert.ThrowsAsync<TimeoutException>(() => zstd.CopyToAsync(output, token));
        Assert.True(output.Length > 0);
    }



    /// <summary>
    /// 只会回 GET 的 HTTP/1.1 服务器，认 <c>Range: bytes=N-</c>，可以让指定的请求送到一半就停住
    /// </summary>
    private sealed class StallingHttpServer : IAsyncDisposable
    {

        public const int ChunkSize = 16 << 10;

        private readonly TcpListener _listener;

        private readonly CancellationTokenSource _cts = new();

        private readonly byte[] _body;

        private readonly Func<int, long?>? _stallAt;

        private readonly TimeSpan _chunkDelay;

        private readonly Task _acceptLoop;

        private readonly ConcurrentBag<Task> _connections = new();

        private readonly List<long?> _rangeStarts = new();


        /// <param name="stallAt">第几个请求（从 0 数）送到哪个位置之后停住，返回 null 表示整个送完</param>
        /// <param name="chunkDelay">每送 <see cref="ChunkSize"/> 之后等多久，模拟很慢但没断的连接</param>
        public StallingHttpServer(byte[] body, Func<int, long?>? stallAt = null, TimeSpan chunkDelay = default)
        {
            _body = body;
            _stallAt = stallAt;
            _chunkDelay = chunkDelay;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Url = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/file");
            _acceptLoop = AcceptLoopAsync();
        }


        public Uri Url { get; }


        /// <summary>
        /// 每个请求的 Range 起点，没带 Range 的是 null
        /// </summary>
        public IReadOnlyList<long?> RangeStarts
        {
            get
            {
                lock (_rangeStarts)
                {
                    return _rangeStarts.ToList();
                }
            }
        }


        private async Task AcceptLoopAsync()
        {
            try
            {
                while (true)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _connections.Add(HandleAsync(client));
                }
            }
            catch (Exception) when (_cts.IsCancellationRequested) { }
        }


        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    NetworkStream stream = client.GetStream();
                    long? rangeStart = await ReadRangeStartAsync(stream, _cts.Token);
                    int index;
                    lock (_rangeStarts)
                    {
                        index = _rangeStarts.Count;
                        _rangeStarts.Add(rangeStart);
                    }
                    long start = rangeStart ?? 0;
                    var header = new StringBuilder();
                    header.Append(rangeStart is null ? "HTTP/1.1 200 OK\r\n" : "HTTP/1.1 206 Partial Content\r\n");
                    header.Append($"Content-Length: {_body.Length - start}\r\n");
                    if (rangeStart is not null)
                    {
                        header.Append($"Content-Range: bytes {start}-{_body.Length - 1}/{_body.Length}\r\n");
                    }
                    header.Append("Content-Type: application/octet-stream\r\n\r\n");
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header.ToString()), _cts.Token);

                    long end = _stallAt?.Invoke(index) is long stall ? Math.Clamp(stall, start, _body.Length) : _body.Length;
                    for (long offset = start; offset < end; offset += ChunkSize)
                    {
                        await stream.WriteAsync(_body.AsMemory((int)offset, (int)Math.Min(ChunkSize, end - offset)), _cts.Token);
                        if (_chunkDelay > TimeSpan.Zero)
                        {
                            await Task.Delay(_chunkDelay, _cts.Token);
                        }
                    }
                    if (end < _body.Length)
                    {
                        // 不关连接也不再送数据，直到测试结束
                        await Task.Delay(Timeout.Infinite, _cts.Token);
                    }
                }
                catch (Exception) when (_cts.IsCancellationRequested) { }
                catch (IOException) { }
            }
        }


        private static async Task<long?> ReadRangeStartAsync(Stream stream, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            long? start = null;
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(cancellationToken)))
            {
                const string prefix = "Range: bytes=";
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    start = long.Parse(line[prefix.Length..].TrimEnd('-'));
                }
            }
            return start;
        }


        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            await _acceptLoop;
            await Task.WhenAll(_connections);
            _cts.Dispose();
        }

    }


}
