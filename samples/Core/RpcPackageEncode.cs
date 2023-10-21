using System.Buffers;
using SuperSocket.ProtoBase;
using System.Buffers.Binary;

namespace Core;

public sealed class RpcPackageEncode : IPackageEncoder<RpcPackageBase>
{
    private const byte HeaderSize = sizeof(short);

    public int Encode(IBufferWriter<byte> writer, RpcPackageBase pack)
    {
        #region 获取头部字节缓冲区

        var headSpan = writer.GetSpan(HeaderSize);
        writer.Advance(HeaderSize);

        #endregion

        #region 写入 command

        var length = writer.WriteLittleEndian((byte)pack.Key);

        #endregion

        #region 写入内容

        length += pack.Encode(writer);

        #endregion

        #region 写入 body 的长度

        BinaryPrimitives.WriteInt16LittleEndian(headSpan, (short)length);

        #endregion

        return HeaderSize + length;
    }
}