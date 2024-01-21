using System.Buffers;
using System.Buffers.Binary;
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

        reader.TryRead(out var command);

        var packetFactory = _packetFactoryPool.Get(command) ?? throw new ProtocolException($"????{command}?????");

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

    protected override RpcPackageBase DecodePackage(ref ReadOnlySequence<byte> buffer)
    {
        return base.DecodePackage(ref buffer);
    }

    protected override int GetBodyLengthFromHeader(ref ReadOnlySequence<byte> buffer)
    {
        short bodyLength;

        if (buffer.IsSingleSegment)
        {
            BinaryPrimitives.TryReadInt16LittleEndian(buffer.FirstSpan, out bodyLength);
            return bodyLength;
        }

        var headSpan = ArrayPool<byte>.Shared.Rent((int)buffer.Length);

        try
        {
            buffer.CopyTo(headSpan);
            BinaryPrimitives.TryReadInt16LittleEndian(headSpan, out bodyLength);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headSpan);
        }
        
        return bodyLength;
    }
}