namespace Starward.Core;

/// <summary>
/// 给下载的响应流加上读取超时：连续 <see cref="DefaultTimeout"/> 收不到任何数据，就抛出 <see cref="TimeoutException"/>。
/// <para/>
/// 网络断过一下之后（路由器的 NAT 表被挤掉、VPN 重连等），已经建立的 TCP 连接常常收不到 RST，
/// 只收数据的这一方永远等不到下一个包，ReadAsync 就一直挂着，下载停在某个百分比、速度为 0。
/// HttpClient.Timeout 在 ResponseHeadersRead 之后就不再管读取正文，所以要自己计时。
/// 超时交给调用方的重试处理，能断点续传的下载会从已经写入的位置接着下载。
/// Polly 和 Setup 的 RetryHelper 默认都不重试 <see cref="OperationCanceledException"/>，所以超时不能抛成取消。
/// <para/>
/// 只在等数据时计时，限速排队、写文件的时间不算在内。只有异步读取计时，同步读取直接交给内层的流。
/// 调用方取消时抛出的一定是带着调用方令牌的 <see cref="OperationCanceledException"/>，不会被当成超时。
/// <para/>
/// Starward.Setup 不引用 Starward.Core，这个文件以链接的方式编译进去。
/// </summary>
public sealed class IdleTimeoutStream : Stream
{

    /// <summary>
    /// 默认的读取超时
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);


    private readonly Stream _stream;

    private readonly TimeSpan _timeout;

    /// <summary>
    /// 与调用方令牌连接、负责计时的令牌源。同一个下载每次读取传的都是同一个令牌，令牌变了才重建
    /// </summary>
    private CancellationTokenSource? _idle;

    private CancellationToken _idleLinkedToken;



    /// <param name="stream">响应流，随本对象一起释放</param>
    /// <param name="timeout">默认 <see cref="DefaultTimeout"/></param>
    public IdleTimeoutStream(Stream stream, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _timeout = timeout ?? DefaultTimeout;
    }



    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_idle is null || _idleLinkedToken != cancellationToken)
        {
            _idle?.Dispose();
            _idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _idleLinkedToken = cancellationToken;
        }
        CancellationTokenSource idle = _idle;
        try
        {
            idle.CancelAfter(_timeout);
            int read = await _stream.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
            idle.CancelAfter(Timeout.InfiniteTimeSpan);
            return read;
        }
        // 取消读取时 HttpClient 会顺手关掉连接，抛出来的不一定是 OperationCanceledException，带的也是这里连接出来的令牌，
        // 所以按令牌判断：调用方取消了就是取消（换成带调用方令牌的异常），调用方没有取消而计时到了才是超时
        catch (Exception ex) when (cancellationToken.IsCancellationRequested && !(ex is OperationCanceledException oce && oce.CancellationToken == cancellationToken))
        {
            throw new TaskCanceledException(ex.Message, ex, cancellationToken);
        }
        catch (Exception ex) when (idle.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No data received for {_timeout.TotalSeconds:0} seconds.", ex);
        }
    }


    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        // 基类的实现会绕到同步的 Read。ZstdSharp 与 SharpCompress 的解压流从内层读的都是这个重载
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }


    public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => _stream.Read(buffer);


    public override bool CanRead => _stream.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stream.Dispose();
            _idle?.Dispose();
        }
        base.Dispose(disposing);
    }


}
