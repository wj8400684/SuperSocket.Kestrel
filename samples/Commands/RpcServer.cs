using Microsoft.Extensions.Options;
using SuperSocket.Server;
using SuperSocket;
using Core;

namespace Commands;

public sealed class RpcServer : SuperSocketService<RpcPackageBase>
{
    public RpcServer(IServiceProvider serviceProvider, IOptions<ServerOptions> serverOptions) : base(serviceProvider, serverOptions)
    {
    }
}
