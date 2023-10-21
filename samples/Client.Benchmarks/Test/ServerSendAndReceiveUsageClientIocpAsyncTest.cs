using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using Core;
using SuperSocket.Client;
using System.Net;

namespace Client.Benchmarks;

/// <summary>
/// 测试服务端
/// 服务端采用原生socket跟iocp
/// 客户端采用原生socket
/// </summary>
[RPlotExporter]
[GcForce(true)]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[SimpleJob(RuntimeMoniker.Net70)]
public class ServerSendAndReceiveUsageClientIocpAsyncTest
{
    //服务端采用原生socket发送接受
    private IEasyClient<RpcPackageBase, RpcPackageBase> _serverSocket = null!;

    //服务端使用iocp发送接受
    private IEasyClient<RpcPackageBase, RpcPackageBase> _serverIcop = null!;

    [GlobalSetup]
    public async ValueTask GlobalSetup()
    {
        _serverSocket = new EasyClient<RpcPackageBase, RpcPackageBase>(new RpcPipeLineFilter(), new RpcPackageEncode());

        await _serverSocket.ConnectAsync(new DnsEndPoint("127.0.0.1", 4040, System.Net.Sockets.AddressFamily.InterNetwork), CancellationToken.None);

        _serverIcop = new EasyClient<RpcPackageBase, RpcPackageBase>(new RpcPipeLineFilter(), new RpcPackageEncode());

        await _serverIcop.ConnectAsync(new DnsEndPoint("127.0.0.1", 4041, System.Net.Sockets.AddressFamily.InterNetwork), CancellationToken.None);
    }

    [Benchmark]
    public async ValueTask UsageServerSocket()
    {
        await _serverSocket.SendAsync(new LoginPackage
        {
            Username = "sss",
            Password = "password",
        });

        var reply = await _serverSocket.ReceiveAsync();
    }

    [Benchmark]
    public async ValueTask UsageServerIocp()
    {
        await _serverIcop.SendAsync(new LoginPackage
        {
            Username = "sss",
            Password = "password",
        });

        var reply = await _serverIcop.ReceiveAsync();
    }
}
