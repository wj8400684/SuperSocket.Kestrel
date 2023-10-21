using Core;
using Microsoft.AspNetCore.Connections;
using SuperSocket.Channel;
using SuperSocket.Kestrel.Channel;

namespace Kestrel.App;

internal sealed class RpcConnectionHandler : ConnectionHandler
{
    private readonly RpcPackageEncode _encode = new();
    private readonly ILogger<RpcConnectionHandler> _logger;

    public RpcConnectionHandler(ILogger<RpcConnectionHandler> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        _logger.LogInformation($"客户端连接：{connection.ConnectionId}-{connection.RemoteEndPoint}");

        var channel = new KestrelPipeChannel<RpcPackageBase>(connection,
                new RpcPipeLineFilter(),
                new ChannelOptions { Logger = _logger });

        try
        {
            channel.Start();

            await foreach (var packet in channel.RunAsync())
            {
                await channel.SendAsync(_encode, new LoginRespPackage
                {
                    SuccessFul = true,
                });
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "");
        }
        finally
        {
            _logger.LogInformation($"客户端断开连接：{connection.ConnectionId}-{connection.RemoteEndPoint}");
            await connection.DisposeAsync();
        }
    }
}
