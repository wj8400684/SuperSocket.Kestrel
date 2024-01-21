using Commands;
using Core.Packages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SuperSocket;
using SuperSocket.Command;
using SuperSocket.Console.Server;
using SuperSocket.IOCPTcpChannelCreatorFactory;

var host = SuperSocketHostBuilder.Create<RpcPackageBase, RpcPipeLineFilter>()
    .UseHostedService<RpcServer>()
    .UseSession<RpcSession>()
    .UsePackageDecoder<RpcPackageDecoder>()
    .UsePackageEncoder<RpcPackageEncode>()
    .UseCommand(options => options.AddCommandAssembly(typeof(Login).Assembly))
    .UseClearIdleSession()
    .UseInProcSessionContainer()
    //.UseIOCPTcpChannelCreatorFactory()
    .UseChannelCreatorFactory<NamedPipeChannelCreatorFactory>()
    .ConfigureServices((context, services) =>
    {
        services.AddLogging();
        services.AddSingleton<IPacketFactoryPool, ReturnPacketFactoryPool>();
    })
    .Build();

await host.RunAsync();