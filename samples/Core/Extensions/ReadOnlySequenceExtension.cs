using SuperSocket.ProtoBase;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Core;

public static class ReadOnlySequenceExtension
{
    public static ReadOnlySequence<byte> CopySequence(ref this ReadOnlySequence<byte> seq)
    {
        SequenceSegment head = null!;
        SequenceSegment tail = null!;

        foreach (var segment in seq)
        {
            var newSegment = SequenceSegment.CopyFrom(segment);

            if (head == null)
                tail = head = newSegment;
            else
                tail = tail.SetNext(newSegment);
        }

        return new ReadOnlySequence<byte>(head, 0, tail, tail.Memory.Length);
    }

    public static List<ArraySegment<byte>> Copy(ref this ReadOnlySequence<byte> seq)
    {
        var body = new List<ArraySegment<byte>>();

        foreach (var memory in seq)
        {
            if (!MemoryMarshal.TryGetArray(memory, out var segment))
                throw new InvalidOperationException("Buffer backed by array was expected");

            body.Add(segment);
        }

        return body;
    }
}
