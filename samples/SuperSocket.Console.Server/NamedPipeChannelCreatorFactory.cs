using SuperSocket.Channel;
using SuperSocket.ProtoBase;

namespace SuperSocket.Console.Server;

public sealed class NamedPipeChannelCreatorFactory : IChannelCreatorFactory
{
    public IChannelCreator CreateChannelCreator<TPackageInfo>(ListenOptions options, ChannelOptions channelOptions,
        ILoggerFactory loggerFactory, object pipelineFilterFactory)
    {
        var filterFactory = pipelineFilterFactory as IPipelineFilterFactory<TPackageInfo>;
        channelOptions.Logger = loggerFactory.CreateLogger(nameof(IChannel));

        var channelFactoryLogger = loggerFactory.CreateLogger(nameof(NamedPipeChannelCreator));
        
        return new NamedPipeChannelCreator(options, (s) => new ValueTask<IChannel>(new NamedPipeStreamChannel<TPackageInfo>(s, filterFactory.Create(s), channelOptions)), channelFactoryLogger);
    }
}