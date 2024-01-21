using System.Buffers;
using SuperSocket.ProtoBase;
using System.Buffers.Binary;

namespace Core;

public sealed class RpcPackageEncode : IPackageEncoder<RpcPackageBase>
{
    private const byte HeaderSize = sizeof(short);

    public int Encode(IBufferWriter<byte> writer, RpcPackageBase pack)
    {
        var headSpan = writer.GetSpan(HeaderSize);
        writer.Advance(HeaderSize);

        var length = writer.WriteLittleEndian((byte)pack.Key);

        length += pack.Encode(writer);

        BinaryPrimitives.WriteInt16LittleEndian(headSpan, (short)length);

        return HeaderSize + length;
    }
}