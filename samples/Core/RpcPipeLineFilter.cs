using System.Buffers;
using Core.Packages;
using SuperSocket.ProtoBase;

namespace Core;

public sealed class RpcPackageDecoder : IPackageDecoder<RpcPackageBase>
{
    private const int HeaderSize = sizeof(short);

    private readonly IPacketFactoryPool _packetFactoryPool;

    public RpcPackageDecoder(IPacketFactoryPool packetFactoryPool)
    {
        _packetFactoryPool = packetFactoryPool;
    }

    public RpcPackageBase Decode(ref ReadOnlySequence<byte> buffer, object context)
    {
        var reader = new SequenceReader<byte>(buffer);

        reader.Advance(HeaderSize);

        //¶ÁÈ¡ command
        reader.TryRead(out var command);

        var packetFactory = _packetFactoryPool.Get(command) ?? throw new ProtocolException($"ÃüÁî£º{command}Î´×¢²á");

        var package = packetFactory.Create();

        package.DecodeBody(ref reader, package);

        return package;
    }
}

/// <summary>
/// | bodyLength | body |
/// | header | cmd | body |
/// </summary>
public sealed class RpcPipeLineFilter : FixedHeaderPipelineFilter<RpcPackageBase>
{
    private const int HeaderSize = sizeof(short);

    public RpcPipeLineFilter()
        : base(HeaderSize)
    {
        Decoder ??= new RpcPackageDecoder(new DefaultPacketFactoryPool());
    }

    protected override int GetBodyLengthFromHeader(ref ReadOnlySequence<byte> buffer)
    {
        var reader = new SequenceReader<byte>(buffer);

        reader.TryReadLittleEndian(out short bodyLength);

        return bodyLength;
    }
}