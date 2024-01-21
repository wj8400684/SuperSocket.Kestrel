using System.Net;
using Microsoft.Extensions.Logging;
using SuperSocket.Channel;
using SuperSocket.Client;
using SuperSocket.Console.Server;
using SuperSocket.ProtoBase;

namespace Client;

public sealed class PipeNameEasyClient<TPackage, TSendPackage> : EasyClient<TPackage, TSendPackage>
    , IEasyClient<TPackage, TSendPackage>
    where TPackage : class
{
    private IPipelineFilter<TPackage> _pipelineFilter;

    public PipeNameEasyClient(IPackageEncoder<TSendPackage> packageEncoder) : base(packageEncoder)
    {
    }

    public PipeNameEasyClient(IPipelineFilter<TPackage> pipelineFilter, IPackageEncoder<TSendPackage> packageEncoder,
        ILogger logger = null) : base(pipelineFilter, packageEncoder, logger)
    {
        _pipelineFilter = pipelineFilter;
    }

    public PipeNameEasyClient(IPipelineFilter<TPackage> pipelineFilter, IPackageEncoder<TSendPackage> packageEncoder,
        ChannelOptions options) : base(pipelineFilter, packageEncoder, options)
    {
    }

    protected override IConnector GetConnector()
    {
        return new PipeNameConnector();
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

        var stream = state.Stream;

        if (stream == null)
            throw new Exception("Socket is null.");

        var channelOptions = Options;
        SetupChannel(new NamedPipeStreamChannel<TPackage>(stream, _pipelineFilter, channelOptions));
        return true;
    }
}