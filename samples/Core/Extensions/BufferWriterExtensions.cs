using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Core;

public static class BufferWriterExtensions
{
    private static void WriteByteBigEndian(Span<byte> destination, byte value)
    {
        MemoryMarshal.Write(destination, ref value);
    }

    private static void WriteByteLittleEndian(Span<byte> destination, byte value)
    {
        if (BitConverter.IsLittleEndian)
            value = BinaryPrimitives.ReverseEndianness(value);
        MemoryMarshal.Write(destination, ref value);
    }


    /// <summary>
    /// 写入byte
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteEndian(this IBufferWriter<byte> writer, byte value)
    {
        const int size = sizeof(byte);

        var span = writer.GetSpan(size);
        WriteByteBigEndian(span, value);
        writer.Advance(size);

        return size;
    }

    /// <summary>
    /// 写入byte
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteLittleEndian(this IBufferWriter<byte> writer, byte value)
    {
        const int size = sizeof(byte);

        var span = writer.GetSpan(size);
        WriteByteLittleEndian(span, value);
        writer.Advance(size);

        return size;
    }

    /// <summary>
    /// 写入bool
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteBigEndian(this IBufferWriter<byte> writer, bool value)
    {
        const int size = sizeof(byte);

        var span = writer.GetSpan(size);
        WriteByteBigEndian(span, value ? (byte)1 : (byte)0);
        writer.Advance(size);

        return size;
    }

    /// <summary>
    /// 写入bool
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteLittleEndian(this IBufferWriter<byte> writer, bool value)
    {
        const int size = sizeof(byte);

        var span = writer.GetSpan(size);
        WriteByteLittleEndian(span, value ? (byte)1 : (byte)0);
        writer.Advance(size);

        return size;
    }

    /// <summary>
    /// 写入int32
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteBigEndian(this IBufferWriter<byte> writer, int value)
    {
        const int size = sizeof(int);

        var span = writer.GetSpan(size);
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        writer.Advance(size);

        return size;
    }

    /// <summary>
    /// 写入int32
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteLittleEndian(this IBufferWriter<byte> writer, int value)
    {
        const int size = sizeof(int);

        var span = writer.GetSpan(size);
        BinaryPrimitives.WriteInt32LittleEndian(span, value);
        writer.Advance(size);

        return size;
    }

    /// <summary>
    /// 写入int16
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteBigEndian(this IBufferWriter<byte> writer, short value)
    {
        const int size = sizeof(short);

        var span = writer.GetSpan(size);
        BinaryPrimitives.WriteInt16BigEndian(span, value);
        writer.Advance(size);

        return size;
    }

    /// <summary>
    /// 写入int16
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteLittleEndian(this IBufferWriter<byte> writer, short value)
    {
        const int size = sizeof(short);

        var span = writer.GetSpan(size);
        BinaryPrimitives.WriteInt16LittleEndian(span, value);
        writer.Advance(size);

        return size;
    }

    /// <summary>
    /// 写入int64
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteBigEndian(this IBufferWriter<byte> writer, long value)
    {
        const int size = sizeof(long);

        var span = writer.GetSpan(size);
        BinaryPrimitives.WriteInt64BigEndian(span, value);
        writer.Advance(size);

        return size;
    }

    /// <summary>
    /// 写入int64
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteLittleEndian(this IBufferWriter<byte> writer, long value)
    {
        const int size = sizeof(long);

        var span = writer.GetSpan(size);
        BinaryPrimitives.WriteInt64LittleEndian(span, value);
        writer.Advance(size);

        return size;
    }

    /// <summary>
    /// 写入uint32
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteBigEndian(this IBufferWriter<byte> writer, uint value)
    {
        const int size = sizeof(uint);

        var span = writer.GetSpan(size);
        BinaryPrimitives.WriteUInt32BigEndian(span, value);
        writer.Advance(size);
        
        return size;
    }

    /// <summary>
    /// 写入uint32
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteLittleEndian(this IBufferWriter<byte> writer, uint value)
    {
        const int size = sizeof(uint);

        var span = writer.GetSpan(size);
        BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        writer.Advance(size);
        
        return size;
    }

    /// <summary>
    /// 写入uint16
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteBigEndian(this IBufferWriter<byte> writer, ushort value)
    {
        const int size = sizeof(ushort);

        var span = writer.GetSpan(size);
        BinaryPrimitives.WriteUInt16BigEndian(span, value);
        writer.Advance(size);
        
        return size;
    }

    /// <summary>
    /// 写入uint16
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteLittleEndian(this IBufferWriter<byte> writer, ushort value)
    {
        const int size = sizeof(ushort);

        var span = writer.GetSpan(size);
        BinaryPrimitives.WriteUInt16LittleEndian(span, value);
        writer.Advance(size);
        
        return size;
    }

    /// <summary>
    /// 写入uint64
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteBigEndian(this IBufferWriter<byte> writer, ulong value)
    {
        const int size = sizeof(ulong);

        var span = writer.GetSpan(size);
        BinaryPrimitives.WriteUInt64BigEndian(span, value);
        writer.Advance(size);
        
        return size;
    }

    /// <summary>
    /// 写入uint64
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="value"></param>
    /// <returns>返回写入的长度</returns>
    public static int WriteLittleEndian(this IBufferWriter<byte> writer, ulong value)
    {
        const int size = sizeof(ulong);

        var span = writer.GetSpan(size);
        BinaryPrimitives.WriteUInt64LittleEndian(span, value);
        writer.Advance(size);
        
        return size;
    }
}