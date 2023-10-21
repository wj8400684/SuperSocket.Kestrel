using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using SuperSocket.Channel;
using SuperSocket;
using System.Net.Sockets;

namespace Kestrel.Abp.Server;

public sealed class TcpTransportFactory : IChannelCreator
{
    private IConnectionListener _connectionListener;
    private CancellationTokenSource _cancellationTokenSource;
    private TaskCompletionSource<bool> _stopTaskCompletionSource;

    private readonly ILogger _logger;
    private readonly SocketTransportFactory _socketTransportFactory;
    private readonly Func<ConnectionContext, ValueTask<IChannel>> _channelFactory;

    public ListenOptions Options { get; }

    public TcpTransportFactory(ListenOptions options, SocketTransportFactory socketTransportFactory, Func<ConnectionContext, ValueTask<IChannel>> channelFactory, ILogger logger)
    {
        Options = options;
        _logger = logger;
        _channelFactory = channelFactory;
        _socketTransportFactory = socketTransportFactory;
    }

    public bool IsRunning { get; private set; }

    public bool Start()
    {
        try
        {
            var listenEndpoint = Options.GetListenEndPoint();

            var result = _socketTransportFactory.BindAsync(listenEndpoint);

            _connectionListener = result.IsCompleted ? result.Result : result.GetAwaiter().GetResult();
            
            IsRunning = true;

            _cancellationTokenSource = new CancellationTokenSource();

            KeepAcceptAsync(_connectionListener).DoNotAwait();
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"The listener[{this.ToString()}] failed to start.");
            return false;
        }
    }

    private async Task KeepAcceptAsync(IConnectionListener connectionListener)
    {
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            try
            {
                var client = await connectionListener.AcceptAsync().ConfigureAwait(false);
                OnNewClientAccept(client);
            }
            catch (Exception e)
            {
                if (e is ObjectDisposedException or NullReferenceException)
                    break;

                if (e is SocketException se)
                {
                    var errorCode = se.ErrorCode;

                    //The listen socket was closed
                    if (errorCode == 125 || errorCode == 89 || errorCode == 995 || errorCode == 10004 || errorCode == 10038)
                    {
                        break;
                    }
                }

                _logger.LogError(e, $"Listener[{this.ToString()}] failed to do AcceptAsync");
                continue;
            }
        }

        _stopTaskCompletionSource.TrySetResult(true);
    }

    public event NewClientAcceptHandler NewClientAccepted;

    private async void OnNewClientAccept(ConnectionContext context)
    {
        var handler = NewClientAccepted;

        if (handler == null)
            return;

        IChannel channel = null;

        try
        {
            channel = await _channelFactory(context);
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Failed to create channel for {context.RemoteEndPoint}.");
            return;
        }

        await handler.Invoke(this, channel);
    }

    public async Task<IChannel> CreateChannel(object connection)
    {
        return await _channelFactory((ConnectionContext)connection);
    }

    public Task StopAsync()
    {
        var listenSocket = _connectionListener;

        if (listenSocket == null)
            return Task.Delay(0);

        _stopTaskCompletionSource = new TaskCompletionSource<bool>();

        _cancellationTokenSource.Cancel();
         _connectionListener.UnbindAsync(CancellationToken.None).DoNotAwait();

        return _stopTaskCompletionSource.Task;
    }

    public override string ToString()
    {
        return Options?.ToString();
    }
}
