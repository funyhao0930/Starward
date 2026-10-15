using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Starward.Core.Tests;

/// <summary>
/// 多个地址的连线方式。
/// <para/>
/// 不通的地址用 192.0.2.1（RFC 5737 的文件专用地址，送出去不会有人回应），
/// 逐个等的实现要卡 21 秒才轮到后面的本机 TcpListener。
/// </summary>
public class FastFallbackConnectorTests
{

    [Fact]
    public async Task DeadFirstAddress_DoesNotWaitForTcpTimeout()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var sw = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using Socket socket = await FastFallbackConnector.ConnectAsync([IPAddress.Parse("192.0.2.1"), IPAddress.Loopback], port, timeout.Token);

        Assert.True(socket.Connected);
        Assert.Equal(IPAddress.Loopback, ((IPEndPoint)socket.RemoteEndPoint!).Address);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"took {sw.Elapsed}");
    }


    [Fact]
    public async Task AllRefused_ThrowsSocketException()
    {
        // 拿到一个空闲埠再关掉，连上去会马上被拒绝
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<SocketException>(() => FastFallbackConnector.ConnectAsync([IPAddress.Loopback, IPAddress.Loopback], port, timeout.Token));
    }


    [Fact]
    public async Task CallerCancellation_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FastFallbackConnector.ConnectAsync([IPAddress.Parse("192.0.2.1"), IPAddress.Parse("192.0.2.2")], 443, cts.Token));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"took {sw.Elapsed}");
    }


    [Fact]
    public void Interleave_AlternatesFamilies()
    {
        IPAddress[] input = [IPAddress.Parse("::1"), IPAddress.Parse("::2"), IPAddress.Parse("1.1.1.1"), IPAddress.Parse("2.2.2.2"), IPAddress.Parse("3.3.3.3")];
        string[] result = Array.ConvertAll(FastFallbackConnector.Interleave(input), a => a.ToString());
        Assert.Equal(["::1", "1.1.1.1", "::2", "2.2.2.2", "3.3.3.3"], result);
    }

}
