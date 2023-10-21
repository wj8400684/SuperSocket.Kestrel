using Commands;
using SuperSocket;
using SuperSocket.Command;
using SuperSocket.IOCPTcpChannelCreatorFactory;
using Core;
using Core.Packages;

var host = SuperSocketHostBuilder.Create<RpcPackageBase, RpcPipeLineFilter>()
    .UseSession<RpcSession>()
    .UsePackageDecoder<RpcPackageDecoder>()
    .UsePackageEncoder<RpcPackageEncode>()
    .UseCommand(options => options.AddCommandAssembly(typeof(Login).Assembly))
    .UseClearIdleSession()
    .UseInProcSessionContainer()
    .UseIOCPTcpChannelCreatorFactory()
    .ConfigureServices((context, services) =>
    {
        services.AddLogging();
        services.AddSingleton<IPacketFactoryPool, ReturnPacketFactoryPool>();
    })
    .Build();

await host.RunAsync();