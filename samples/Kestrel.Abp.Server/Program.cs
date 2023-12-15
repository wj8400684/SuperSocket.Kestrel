using Kestrel.Abp.Server;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using SuperSocket;
using SuperSocket.Command;
using SuperSocket.ProtoBase;
using Commands;
using Core.Packages;
using SuperSocket.IOCPTcpChannelCreatorFactory;

//var host = SuperSocketHostBuilder.Create<RpcPackageBase, RpcPipeLineFilter>()
//    .UseHostedService<RpcServer>()
//    .UseSession<RpcSession>()
//    .UsePackageDecoder<RpcPackageDecoder>()
//    .UseCommand(options => options.AddCommandAssembly(typeof(Login).Assembly))
//    .UseClearIdleSession()
//    .UseInProcSessionContainer()
//    .UseIOCPTcpChannelCreatorFactory()
//    .ConfigureServices((context, services) =>
//    {
//        services.AddLogging();
//        services.AddSingleton<IPackageEncoder<RpcPackageBase>, RpcPackageEncode>();
//        services.AddSingleton<IPacketFactoryPool, DefaultPacketFactoryPool>();
//    })
//    .Build();

//await host.RunAsync();

var builder = WebApplication.CreateBuilder(args);

//builder.WebHost.ConfigureKestrel((context, options) =>
//{
//    var serverOptions = context.Configuration.GetSection("ServerOptions").Get<ServerOptions>()!;

//    foreach (var listeners in serverOptions.Listeners)
//    {
//        options.Listen(listeners.GetListenEndPoint(), listenOptions =>
//        {
//            listenOptions.UseConnectionHandler<KestrelChannelCreator>();
//        });
//    }
//});

builder.Host.AsSuperSocketHostBuilder<RpcPackageBase, RpcPipeLineFilter>()
            .UseHostedService<RpcServer>()
            .UseSession<RpcSession>()
            .UsePackageDecoder<RpcPackageDecoder>()
            .UsePackageEncoder<RpcPackageEncode>()
            .UseCommand(options => options.AddCommandAssembly(typeof(Login).Assembly))
            .UseClearIdleSession()
            .UseInProcSessionContainer()
            //.UseIOCPTcpChannelCreatorFactory()
            //.UseChannelCreatorFactory<Kestrel.Abp.Server.TcpIocpChannelCreatorFactory>()
            //.UseKestrelChannelCreatorFactory()
            .AsMinimalApiHostBuilder()
            .ConfigureHostBuilder();

builder.Services.AddLogging(s => s.AddConsole().AddDebug());
builder.Services.AddSingleton<IPacketFactoryPool, DefaultPacketFactoryPool>();

var app = builder.Build();

await app.RunAsync();
