using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using Core;
using SuperSocket.Client;
using System.Net;
using SuperSocket.IOCPEasyClient;

namespace Client.Benchmarks;

/// <summary>
/// 测试客户端
/// 服务端采用iocp
/// 客户端使用socket跟iocp
/// </summary>
[RPlotExporter]
[GcForce(true)]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[SimpleJob(RuntimeMoniker.Net70)]
public class ClientSendAndReceiveUsageServerIocpAsyncTest
{
    //服务端采用原生socket发送接受
    private IEasyClient<RpcPackageBase, RpcPackageBase> _serverSocket = null!;

    //服务端使用iocp发送接受
    private IEasyClient<RpcPackageBase, RpcPackageBase> _serverIcop = null!;

    [GlobalSetup]
    public async ValueTask GlobalSetup()
    {
        _serverSocket = new EasyClient<RpcPackageBase, RpcPackageBase>(new RpcPipeLineFilter(), new RpcPackageEncode());

        await _serverSocket.ConnectAsync(new DnsEndPoint("127.0.0.1", 4041, System.Net.Sockets.AddressFamily.InterNetwork), CancellationToken.None);

        _serverIcop = new IOCPTcpEasyClient<RpcPackageBase, RpcPackageBase>(new RpcPipeLineFilter(), new RpcPackageEncode());

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
