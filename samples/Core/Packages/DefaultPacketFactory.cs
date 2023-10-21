using System.Collections.Concurrent;

namespace Core.Packages;

public interface IPacketFactory
{
    RpcPackageBase Create();

    void Return(RpcPackageBase package);
}

public sealed class DefaultPacketFactory<TPacket> : IPacketFactory
    where TPacket : RpcPackageBase, new()
{
    public RpcPackageBase Create()
    {
        return new TPacket();
    }

    public void Return(RpcPackageBase package)
    {
    }
}

public sealed class ReturnPacketFactory<TPacket> : IPacketFactory
    where TPacket : RpcPackageBase, new()
{
    private const int DefaultMaxCount = 10;
    private readonly ConcurrentQueue<RpcPackageBase> _packagePool = new();

    public ReturnPacketFactory()
    {
        for (int i = 0; i < DefaultMaxCount; i++)
        {
            var packet = new TPacket();

            packet.Inilizetion(this);

            _packagePool.Enqueue(packet);
        }
    }

    public RpcPackageBase Create()
    {
        if (_packagePool.TryDequeue(out var package))
            return package;

        var packet = new TPacket();

        packet.Inilizetion(this);

        return packet;
    }

    public void Return(RpcPackageBase package)
    {
        _packagePool.Enqueue(package);
    }
}
