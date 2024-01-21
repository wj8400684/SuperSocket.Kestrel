using System.IO.Pipes;
using System.Net;
using System.Security.Principal;
using SuperSocket.Client;
using SuperSocket.Console.Server;

namespace Client;

public sealed class PipeNameConnector : ConnectorBase
{
    protected override async ValueTask<ConnectState> ConnectAsync(EndPoint remoteEndPoint, ConnectState state,
        CancellationToken cancellationToken)
    {
        if (remoteEndPoint is not NamedPipeEndPoint namedPipeEndPoint)
            throw new NotSupportedException($"{remoteEndPoint.GetType()} is not supported");
        
        var client = new NamedPipeClientStream(
            serverName: namedPipeEndPoint.ServerName,
            pipeName: namedPipeEndPoint.PipeName,
            direction: PipeDirection.InOut,
            options: namedPipeEndPoint.PipeOptions,
            impersonationLevel: TokenImpersonationLevel.None,
            inheritability: HandleInheritability.None);

        await client.ConnectAsync(cancellationToken);

        return new ConnectState
        {
            Stream = client,
            Result = true,
        };
    }
}