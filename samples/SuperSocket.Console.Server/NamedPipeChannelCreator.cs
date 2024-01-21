using System.IO.Pipes;
using SuperSocket.Channel;

namespace SuperSocket.Console.Server;

public sealed class NamedPipeChannelCreator : IChannelCreator
{
    private CancellationTokenSource _cancellationTokenSource;

    private readonly ILogger _logger;
    private readonly Func<Stream, ValueTask<IChannel>> _channelFactory;

    public bool IsRunning { get; private set; }
    public ListenOptions Options { get; }
    public event NewClientAcceptHandler NewClientAccepted;

    public NamedPipeChannelCreator(ListenOptions options, Func<Stream, ValueTask<IChannel>> channelFactory,
        ILogger logger)
    {
        Options = options;
        _logger = logger;
        _channelFactory = channelFactory;
    }

    public bool Start()
    {
        var options = Options;

        ArgumentException.ThrowIfNullOrEmpty(options.Path,"Path is not null");
        
        try
        {
            IsRunning = true;

            _cancellationTokenSource = new CancellationTokenSource();

            KeepAccept(options.Path).DoNotAwait();
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"The listener[{this.ToString()}] failed to start.");
            return false;
        }
    }


    public async Task<IChannel> CreateChannel(object connection)
    {
        return await _channelFactory((Stream)connection);
    }

    public Task StopAsync()
    {
        _cancellationTokenSource.Cancel();
        return default;
    }

    private async Task KeepAccept(string serverName)
    {
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            var stream = new NamedPipeServerStream(
                pipeName: serverName,
                direction: PipeDirection.InOut,
                maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
                transmissionMode: PipeTransmissionMode.Byte,
                options: PipeOptions.Asynchronous);
            
            try
            {
                await stream.WaitForConnectionAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Listener[{this.ToString()}] failed to do WaitForConnectionAsync");
                continue;
            }
            
            OnNewClientAccept(stream);
        }
    }

    private async void OnNewClientAccept(NamedPipeServerStream stream)
    {
        var handler = NewClientAccepted;

        if (handler == null)
            return;

        IChannel channel = null;

        try
        {
            channel = await _channelFactory(stream);
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Failed to create channel for {Options.Path}.");
            return;
        }

        await handler.Invoke(this, channel);
    }
}