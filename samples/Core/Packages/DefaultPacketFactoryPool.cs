namespace Core.Packages;

public interface IPacketFactoryPool
{
    IPacketFactory Get(CommandKey command);

    IPacketFactory? Get(byte command);
}

public class DefaultPacketFactoryPool : IPacketFactoryPool
{
    private readonly IPacketFactory[] _packetFactories;

    public DefaultPacketFactoryPool()
    {
        _packetFactories = Inilizetion();
    }

    protected virtual IPacketFactory[] Inilizetion()
    {
        var commands = RpcPackageBase.GetCommands();

        var packetFactories = new IPacketFactory[commands.Count + 1];

        foreach (var command in commands)
        {
            var genericType = typeof(DefaultPacketFactory<>).MakeGenericType(command.Key);

            if (Activator.CreateInstance(genericType) is not IPacketFactory packetFactory)
                continue;

            packetFactories[(int)command.Value] = packetFactory;
        }

        return packetFactories;
    }

    public IPacketFactory Get(CommandKey command)
    {
        return Get((byte)command)!;
    }

    public IPacketFactory? Get(byte command)
    {
        if (command > _packetFactories.Length)
            return null;

        return _packetFactories[command];
    }
}

public sealed class ReturnPacketFactoryPool : DefaultPacketFactoryPool
{
    protected override IPacketFactory[] Inilizetion()
    {
        var commands = RpcPackageBase.GetCommands();

        var packetFactories = new IPacketFactory[commands.Count + 1];

        foreach (var command in commands)
        {
            var genericType = typeof(ReturnPacketFactory<>).MakeGenericType(command.Key);

            if (Activator.CreateInstance(genericType) is not IPacketFactory packetFactory)
                continue;

            packetFactories[(int)command.Value] = packetFactory;
        }

        return packetFactories;
    }
}
