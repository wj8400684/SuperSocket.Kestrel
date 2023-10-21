using Microsoft.AspNetCore.Connections;

namespace Kestrel.App;

internal sealed class EchoConnectionHandler : ConnectionHandler
{
    private readonly ILogger<RpcConnectionHandler> _logger;

    public EchoConnectionHandler(ILogger<RpcConnectionHandler> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        _logger.LogInformation($"客户端连接：{connection.ConnectionId}-{connection.RemoteEndPoint}");

        try
        {
            await HandleRequestsAsync(connection);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex.Message);
        }
        finally
        {
            _logger.LogInformation($"客户端断开连接：{connection.ConnectionId}-{connection.RemoteEndPoint}");
            await connection.DisposeAsync();
        }
    }

    private async Task HandleRequestsAsync(ConnectionContext context)
    {
        var reader = context.Transport.Input;
        var writer = context.Transport.Output;

        while (context.ConnectionClosed.IsCancellationRequested == false)
        {
            var result = await reader.ReadAsync();
            if (result.IsCanceled)
                break;

            if (result.IsCompleted)
                break;
        }
    }
}
