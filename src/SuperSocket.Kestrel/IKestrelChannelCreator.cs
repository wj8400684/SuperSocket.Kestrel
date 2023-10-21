using Microsoft.AspNetCore.Connections;
using SuperSocket.Channel;

namespace SuperSocket.Kestrel;

public interface IKestrelChannelCreator : IChannelCreator
{
    event Func<ConnectionContext, ValueTask<IChannel>> ChannelFactoryAsync;
}