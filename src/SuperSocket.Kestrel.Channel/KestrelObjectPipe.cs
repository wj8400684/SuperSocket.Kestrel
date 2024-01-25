using System.Buffers;
using System.Threading.Tasks.Sources;

namespace SuperSocket.Kestrel.Internal;

public sealed class KestrelObjectPipe<T> : IValueTaskSource<T>, IDisposable
{
    private class BufferSegment
    {
        public T[] Array { get; private set; }

        public BufferSegment Next { get; set; }

        public int Offset { get; set; }

        public int End { get; set; } = -1; // -1 means no item in this segment

        public BufferSegment(T[] array)
        {
            Array = array;
        }

        public bool IsAvailable => Array.Length > End + 1;

        public void Write(T value)
        {
            Array[++End] = value;
        }
    }

    private const int SegmentSize = 5;
    private BufferSegment _first;
    private BufferSegment _current;
    private readonly object _syncRoot = new();
    private static readonly ArrayPool<T> Pool = ArrayPool<T>.Shared;
    private ManualResetValueTaskSourceCore<T> _taskSourceCore;
    private bool _waiting;
    private bool _lastReadIsWait;
    private int _length;

    public int Count => _length;

    public KestrelObjectPipe()
    {
        SetBufferSegment(CreateSegment());
        _taskSourceCore = new ManualResetValueTaskSourceCore<T>();
    }

    #region public

    public int Write(T target)
    {
        lock (_syncRoot)
        {
            if (_waiting)
            {
                _waiting = false;
                _taskSourceCore.SetResult(target);
                return _length;
            }

            var current = _current;

            if (!current.IsAvailable)
            {
                current = CreateSegment();
                SetBufferSegment(current);
            }

            current.Write(target);
            _length++;
            return _length;
        }
    }

    private bool TryRead(out T value)
    {
        var first = _first;

        if (first.Offset < first.End)
        {
            value = first.Array[first.Offset];
            first.Array[first.Offset] = default;
            first.Offset++;
            return true;
        }
        else if (first.Offset == first.End)
        {
            if (first == _current)
            {
                value = first.Array[first.Offset];
                first.Array[first.Offset] = default;
                first.Offset = 0;
                first.End = -1;
                return true;
            }
            else
            {
                value = first.Array[first.Offset];
                first.Array[first.Offset] = default;
                _first = first.Next;
                Pool.Return(first.Array);
                return true;
            }
        }

        value = default;
        return false;
    }

    public ValueTask<T> ReadAsync()
    {
        lock (_syncRoot)
        {
            if (TryRead(out T value))
            {
                if (_lastReadIsWait)
                {
                    // clear the result saved previously in the taskSource object
                    _taskSourceCore.Reset();
                    _lastReadIsWait = false;
                }

                _length--;

                return new ValueTask<T>(value);
            }

            _waiting = true;
            _lastReadIsWait = true;
            _taskSourceCore.Reset();

            return new ValueTask<T>(this, _taskSourceCore.Version);
        }
    }

    #endregion

    #region private

    private static BufferSegment CreateSegment()
    {
        return new BufferSegment(Pool.Rent(SegmentSize));
    }

    private void SetBufferSegment(BufferSegment segment)
    {
        _first ??= segment;

        var current = _current;

        if (current != null)
            current.Next = segment;

        _current = segment;
    }

    #endregion

    #region interface

    T IValueTaskSource<T>.GetResult(short token)
    {
        return _taskSourceCore.GetResult(token);
    }

    ValueTaskSourceStatus IValueTaskSource<T>.GetStatus(short token)
    {
        return _taskSourceCore.GetStatus(token);
    }

    void IValueTaskSource<T>.OnCompleted(Action<object> continuation, object state, short token,
        ValueTaskSourceOnCompletedFlags flags)
    {
        _taskSourceCore.OnCompleted(continuation, state, token, flags);
    }

    #endregion

    #region IDisposable Support

    private bool _disposedValue; // To detect redundant calls

    private void Dispose(bool disposing)
    {
        if (_disposedValue)
            return;

        if (disposing)
        {
            lock (_syncRoot)
            {
                // return all segments into the pool
                var segment = _first;

                while (segment != null)
                {
                    Pool.Return(segment.Array);
                    segment = segment.Next;
                }

                _first = null;
                _current = null;
            }
        }

        _disposedValue = true;
    }

    void IDisposable.Dispose()
    {
        Dispose(true);
    }

    #endregion
}