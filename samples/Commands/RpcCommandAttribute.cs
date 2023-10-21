using Core;
using SuperSocket.Command;

namespace Commands;

public sealed class RpcCommandAttribute : CommandAttribute
{
    public CommandKey Command { get; }

    public RpcCommandAttribute(CommandKey key)
    {
        Key = (byte)key;
        Command = key;
    }
}
