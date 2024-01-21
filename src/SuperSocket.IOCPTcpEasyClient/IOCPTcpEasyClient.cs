using Microsoft.Extensions.Logging;
using SuperSocket.Channel;
using SuperSocket.Client;
using SuperSocket.IOCPTcpChannel;
using SuperSocket.ProtoBase;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Server.PipeLine;

namespace SuperSocket.IOCPEasyClient;

public class IOCPTcpEasyClient<TPackage, TSendPackage> : IOCPTcpEasyClient<TPackage>,
    IEasyClient<TPackage, TSendPackage>
    where TPackage : class
{
    private readonly IPackageEncoder<TSendPackage> _packageEncoder;

    public IOCPTcpEasyClient(IPipelineFilter<TPackage> pipelineFilter, IPackageEncoder<TSendPackage> packageEncoder,
        ILogger? logger = null)
        : this(pipelineFilter, packageEncoder, new ChannelOptions { Logger = logger })
    {
    }

    public IOCPTcpEasyClient(IPipelineFilter<TPackage> pipelineFilter, IPackageEncoder<TSendPackage> packageEncoder,
        ChannelOptions options)
        : base(pipelineFilter, options)
    {
        _packageEncoder = packageEncoder;
    }

    public virtual ValueTask SendAsync(TSendPackage package)
    {
        return SendAsync(_packageEncoder, package);
    }
}

public class IOCPTcpEasyClient<TPackage> : EasyClient<TPackage>
    where TPackage : class
{
    private readonly IPipelineFilter<TPackage> _pipelineFilter;

    public IOCPTcpEasyClient(IPipelineFilter<TPackage> pipelineFilter)
        : this(pipelineFilter, new ChannelOptions())
    {
    }

    public IOCPTcpEasyClient(IPipelineFilter<TPackage> pipelineFilter, ILogger? logger)
        : this(pipelineFilter, new ChannelOptions
        {
            Logger = logger
        })
    {
    }

    public IOCPTcpEasyClient(IPipelineFilter<TPackage> pipelineFilter, ChannelOptions options)
        : base(pipelineFilter, options)
    {
        _pipelineFilter = pipelineFilter;
    }

    protected virtual IOCPTcpPipeChannel<TPackage> CreatIocpTcpPipeChannel(Socket socket,
        IPipelineFilter<TPackage> pipelineFilter, ChannelOptions options)
    {
        return new IOCPTcpPipeChannel<TPackage>(socket, pipelineFilter, options,
            new SocketSenderPool(PipeScheduler.ThreadPool), PipeScheduler.ThreadPool);
    }

    protected override async ValueTask<bool> ConnectAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken)
    {
        var connector = GetConnector();
        var state = await connector.ConnectAsync(remoteEndPoint, null, cancellationToken);

        if (state.Cancelled || cancellationToken.IsCancellationRequested)
        {
            OnError($"The connection to {remoteEndPoint} was cancelled.", state.Exception);
            return false;
        }

        if (!state.Result)
        {
            OnError($"Failed to connect to {remoteEndPoint}", state.Exception);
            return false;
        }

        var channelOptions = Options;
        var socket = state.Socket ?? throw new Exception("Socket is null.");

        var channel = CreatIocpTcpPipeChannel(socket, _pipelineFilter, channelOptions);

        SetupChannel(channel);
        
        return true;
    }
}