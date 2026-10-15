using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Starward.Core;

/// <summary>
/// 给 <see cref="SocketsHttpHandler.ConnectCallback"/> 用的连线方式：域名解析出多个地址时，
/// 不再一个一个等，前一个 <see cref="AttemptDelay"/> 内没连上就同时试下一个，先连上的赢（RFC 8305 的做法）。
/// <para/>
/// .NET 自带的连线是照 DNS 回传的顺序逐个试，前一个地址失败了才换下一个。
/// 遇到一个不通的地址（只丢包不回 RST）要等 Windows 的 TCP 连线超时，大约 21 秒，
/// 比很多调用方自己设的超时还长，结果就是「第一次失败、重试就好」：
/// 例如 as.gryphline.com 轮流回传的两个 EdgeOne 地址里有一个从某些网络连不上，
/// DNS 刚好把它排在前面时，终末地角色卡片的查询就会超时。
/// </summary>
public static class FastFallbackConnector
{

    /// <summary>
    /// 上一个地址还没连上时，隔多久开始试下一个。RFC 8305 建议 250 毫秒
    /// </summary>
    public static readonly TimeSpan AttemptDelay = TimeSpan.FromMilliseconds(250);



    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        DnsEndPoint endPoint = context.DnsEndPoint;
        IPAddress[] addresses = IPAddress.TryParse(endPoint.Host, out IPAddress? literal)
            ? [literal]
            : Interleave(await Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken).ConfigureAwait(false));
        if (addresses.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }
        Socket socket = await ConnectAsync(addresses, endPoint.Port, cancellationToken).ConfigureAwait(false);
        return new NetworkStream(socket, ownsSocket: true);
    }



    internal static async Task<Socket> ConnectAsync(IReadOnlyList<IPAddress> addresses, int port, CancellationToken cancellationToken)
    {
        using var attemptsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = new List<Task<Socket>>();
        Exception? lastError = null;
        int next = 0;
        try
        {
            pending.Add(ConnectOneAsync(addresses[next++], port, attemptsCts.Token));
            while (pending.Count > 0)
            {
                // 还有地址没试时多等一个计时器
                Task finished = next < addresses.Count
                    ? await Task.WhenAny([.. pending, Task.Delay(AttemptDelay, attemptsCts.Token)]).ConfigureAwait(false)
                    : await Task.WhenAny(pending).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (finished is Task<Socket> attempt)
                {
                    pending.Remove(attempt);
                    if (attempt.IsCompletedSuccessfully)
                    {
                        return attempt.Result;
                    }
                    lastError = attempt.Exception?.InnerException ?? lastError;
                }
                // 计时器到了，或者这个地址明确失败了，都不必再等，开始试下一个
                if (next < addresses.Count)
                {
                    pending.Add(ConnectOneAsync(addresses[next++], port, attemptsCts.Token));
                }
            }
        }
        finally
        {
            // 输掉的连线全部放弃；刚好也连上的要关掉，失败的要读一下例外，免得变成未观察的例外
            attemptsCts.Cancel();
            foreach (Task<Socket> loser in pending)
            {
                _ = loser.ContinueWith(static t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        t.Result.Dispose();
                    }
                    else
                    {
                        _ = t.Exception;
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        throw lastError ?? new SocketException((int)SocketError.HostNotFound);
    }



    private static async Task<Socket> ConnectOneAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken).ConfigureAwait(false);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }



    /// <summary>
    /// IPv6 与 IPv4 交错排列，保持各自原本的顺序，其中一族整个不通时另一族也能很快轮到
    /// </summary>
    internal static IPAddress[] Interleave(IPAddress[] addresses)
    {
        var v6 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).ToList();
        var v4 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToList();
        if (v6.Count == 0 || v4.Count == 0)
        {
            return addresses;
        }
        var result = new List<IPAddress>(addresses.Length);
        for (int i = 0; i < Math.Max(v6.Count, v4.Count); i++)
        {
            if (i < v6.Count) result.Add(v6[i]);
            if (i < v4.Count) result.Add(v4[i]);
        }
        return [.. result];
    }

}
