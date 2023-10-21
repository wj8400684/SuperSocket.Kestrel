namespace Core;

public sealed class PacketIdentifierProvider
{
    private readonly object _syncRoot = new();

    private ulong _value;

    public void Reset()
    {
        lock (_syncRoot)
        {
            _value = 0;
        }
    }

    public ulong GetNextPacketIdentifier()
    {
        lock (_syncRoot)
        {
            _value++;

            if (_value == 0)
                _value = 1;

            return _value;
        }
    }
}