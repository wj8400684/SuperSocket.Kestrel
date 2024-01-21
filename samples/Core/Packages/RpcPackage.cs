using Core.Packages;
using MemoryPack;
using SuperSocket.ProtoBase;
using System.Buffers;

namespace Core;


public abstract class RpcPackageBase : IKeyedPackageInfo<CommandKey>, IDisposable
{
    protected readonly Type Type;
    private IPacketFactory? _packetFactory;
    private static readonly Dictionary<Type, CommandKey> CommandTypes = new();

    #region command inilizetion

    internal static void LoadAllCommand()
    {
        var packets = typeof(RpcPackageBase).Assembly.GetTypes()
            .Where(t => typeof(RpcPackageBase).IsAssignableFrom(t))
            .Where(t => !t.IsAbstract && t.IsClass)
            .Select(t => (RpcPackageBase?)Activator.CreateInstance(t));

        using var enumerator = packets.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Current != null)
                CommandTypes.TryAdd(enumerator.Current.GetType(), enumerator.Current.Key);
        }
    }

    public static CommandKey GetCommandKey<TPacket>()
    {
        var type = typeof(TPacket);

        if (!CommandTypes.TryGetValue(type, out var key))
            throw new Exception($"{type.Name} δ�̳�PlayPacket");

        return key;
    }

    public static List<KeyValuePair<Type, CommandKey>> GetCommands()
    {
        return CommandTypes.ToList();
    }

    static RpcPackageBase()
    {
        LoadAllCommand();
    }

    #endregion

    protected RpcPackageBase(CommandKey key)
    {
        Key = key;
        Type = GetType();
    }

    /// <summary>
    /// 命令
    /// </summary>
    [MemoryPackIgnore]
    public CommandKey Key { get; set; }

    public void Inilizetion(IPacketFactory factory)
    {
        _packetFactory = factory;
    }

    public virtual int Encode(IBufferWriter<byte> bufWriter)
    {
        using var state = MemoryPackWriterOptionalStatePool.Rent(MemoryPackSerializerOptions.Utf8);
        var writer = new MemoryPackWriter<IBufferWriter<byte>>(ref bufWriter, state);
        writer.WriteValue(Type, this);
        var writtenCount = writer.WrittenCount;
        writer.Flush();

        return writtenCount;
    }

    public virtual void DecodeBody(ref SequenceReader<byte> reader, object? context)
    {
        MemoryPackSerializer.Deserialize(Type, reader.UnreadSequence, ref context);
    }

    public override string ToString()
    {
        return System.Text.Json.JsonSerializer.Serialize(this, Type);
    }

    public virtual void Dispose()
    {
        _packetFactory?.Return(this);
    }
}

public abstract class RpcRespPackage : RpcPackageBase
{
    public string? ErrorMessage { get; set; }

    public bool SuccessFul { get; set; }

    public int ErrorCode { get; set; }

    public override void Dispose()
    {
        ErrorMessage = default;
        SuccessFul = default;
        ErrorCode = default;
    }

    protected RpcRespPackage(CommandKey key)
        : base(key)
    {
    }
}

public abstract class RpcPackageWithIdentifier : RpcPackageBase
{
    public ulong Identifier { get; set; }

    public override void Dispose()
    {
        Identifier = default;
        base.Dispose();
    }

    protected RpcPackageWithIdentifier(CommandKey key) : base(key)
    {
    }
}

public abstract class RpcRespPackageWithIdentifier : RpcPackageWithIdentifier
{
    public string? ErrorMessage { get; set; }

    public bool SuccessFul { get; set; }

    public int ErrorCode { get; set; }

    public override void Dispose()
    {
        ErrorMessage = default;
        SuccessFul = default;
        ErrorCode = default;
        base.Dispose();
    }

    protected RpcRespPackageWithIdentifier(CommandKey key) : base(key)
    {
    }
}