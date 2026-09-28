using Starward.Core.Launcher;
using Starward.Core.Launcher.Gryphline;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Starward.Core.Tests.Launcher;

/// <summary>
/// 终末地按文件安装的准备阶段：从分卷末尾按 Range 读 zip 中央目录。
/// <para/>
/// 用假的 HttpMessageHandler 当 CDN，一个真的 zip 切成两卷，末尾那段横跨两卷，
/// 两卷都要读到。读取超时调成 1 秒，正式代码是 <see cref="IdleTimeoutStream.DefaultTimeout"/>。
/// </summary>
public class GryphlinePackageEntriesTests
{

    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(1);

    private const int TestTimeout = 30_000;


    private static (byte[] Zip, List<string> Names) CreateZip()
    {
        var random = new Random(42);
        var names = new List<string>();
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < 8; i++)
            {
                string name = $"EndField_Data/file{i}.bin";
                names.Add(name);
                byte[] data = new byte[20_000];
                random.NextBytes(data);
                using Stream entry = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                entry.Write(data);
            }
        }
        return (ms.ToArray(), names);
    }


    private static GryphlineGamePackage CreatePackage(byte[] zip, out Dictionary<Uri, byte[]> packs)
    {
        // 切在末尾 30000 字节处：末尾约 64 KB 的那段一半在第一卷、一半在第二卷
        int split = zip.Length - 30_000;
        packs = new Dictionary<Uri, byte[]>
        {
            [new Uri("https://cdn.test/EndField.zip.001")] = zip[..split],
            [new Uri("https://cdn.test/EndField.zip.002")] = zip[split..],
        };
        return new GryphlineGamePackage
        {
            Packs = packs.Select(x => new GryphlinePackageFile { Url = x.Key.ToString(), PackageSize = x.Value.Length.ToString() }).ToList(),
        };
    }



    [Theory(Timeout = TestTimeout)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetPackageEntries_ReadsTheCentralDirectoryAcrossPacks(bool ignoreRange)
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        (byte[] zip, List<string> names) = CreateZip();
        GryphlineGamePackage package = CreatePackage(zip, out var packs);
        // ignoreRange：CDN 不认 Range，回整个分卷（200），只取要的那一段
        using var handler = new FakeCdnHandler(packs) { IgnoreRange = ignoreRange };
        var client = new GryphlineLauncherClient(new HttpClient(handler)) { PackageReadTimeout = IdleTimeout };

        IReadOnlyList<ZipEntryInfo> entries = await client.GetPackageEntriesAsync(package, token);

        Assert.Equal(names, entries.Select(x => x.Name));
        Assert.All(entries, x => Assert.Equal(20_000, x.Size));
        Assert.Equal(packs.Keys.Select(x => x.ToString()).Order(), handler.RequestedUrls.Distinct().Select(x => x.ToString()).Order());
    }



    [Fact(Timeout = TestTimeout)]
    public async Task GetPackageEntries_ShortBody_ThrowsInvalidDataException()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        (byte[] zip, _) = CreateZip();
        GryphlineGamePackage package = CreatePackage(zip, out var packs);
        using var handler = new FakeCdnHandler(packs) { SendAtMost = 1000, CloseAfterSend = true };
        var client = new GryphlineLauncherClient(new HttpClient(handler)) { PackageReadTimeout = IdleTimeout };

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetPackageEntriesAsync(package, token));
    }



    [Fact(Timeout = TestTimeout)]
    public async Task GetPackageEntries_StalledConnection_ThrowsTimeoutException()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        (byte[] zip, _) = CreateZip();
        GryphlineGamePackage package = CreatePackage(zip, out var packs);
        // 送完响应头与 1000 字节之后既不关连接也不再送数据
        using var handler = new FakeCdnHandler(packs) { SendAtMost = 1000 };
        var client = new GryphlineLauncherClient(new HttpClient(handler)) { PackageReadTimeout = IdleTimeout };

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() => client.GetPackageEntriesAsync(package, token));
        Assert.InRange(stopwatch.Elapsed, IdleTimeout * 0.9, IdleTimeout * 5);
    }



    /// <summary>
    /// 认 <c>Range: bytes=N-M</c> 的假 CDN，可以只送一部分正文就停住或关掉
    /// </summary>
    private sealed class FakeCdnHandler(Dictionary<Uri, byte[]> packs) : HttpMessageHandler
    {

        /// <summary>
        /// 不认 Range，回整个分卷（200）
        /// </summary>
        public bool IgnoreRange { get; init; }

        /// <summary>
        /// 每个响应最多送多少字节正文
        /// </summary>
        public int SendAtMost { get; init; } = int.MaxValue;

        /// <summary>
        /// 送到 <see cref="SendAtMost"/> 之后关掉（正文变短），否则停住不动
        /// </summary>
        public bool CloseAfterSend { get; init; }

        public List<Uri> RequestedUrls { get; } = new();


        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri url = request.RequestUri!;
            byte[] pack = packs[url];
            lock (RequestedUrls)
            {
                RequestedUrls.Add(url);
            }
            var status = HttpStatusCode.OK;
            byte[] body = pack;
            if (!IgnoreRange && request.Headers.Range?.Ranges.Single() is { From: long from, To: long to })
            {
                status = HttpStatusCode.PartialContent;
                body = pack[(int)from..(int)(to + 1)];
            }
            Stream stream = new PartialStream(body, SendAtMost, CloseAfterSend);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StreamContent(stream), RequestMessage = request });
        }

    }


    private sealed class PartialStream(byte[] data, int limit, bool closeAfterLimit) : Stream
    {

        private int _position;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int end = Math.Min(data.Length, limit);
            if (_position >= end)
            {
                if (end == data.Length || closeAfterLimit)
                {
                    return 0;
                }
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            int count = Math.Min(buffer.Length, end - _position);
            data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Only async reads.");

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    }


}
