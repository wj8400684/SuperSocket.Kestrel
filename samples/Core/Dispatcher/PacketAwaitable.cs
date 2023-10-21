namespace Core;

public sealed class PacketAwaitable<TPacket> : IPacketAwaitable where TPacket : RpcPackageBase
{
    private readonly TaskCompletionSource<RpcPackageBase> _promise = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly PacketDispatcher _owningPacketDispatcher;
    private readonly ulong _packetIdentifier;
    
    public PacketAwaitable(ulong packetIdentifier, PacketDispatcher owningPacketDispatcher)
    {
        _packetIdentifier = packetIdentifier;
        _owningPacketDispatcher = owningPacketDispatcher ?? throw new ArgumentNullException(nameof(owningPacketDispatcher));
    }
        
    public async Task<TPacket> WaitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var register = cancellationToken.Register(() => Fail(new TimeoutException()));
        var packet = await _promise.Task.ConfigureAwait(false);
        return (TPacket)packet;
    }

    public void Complete(RpcPackageBase packet)
    {
        if (packet == null) 
            throw new ArgumentNullException(nameof(packet));

        _promise.TrySetResult(packet);
    }

    public void Fail(Exception exception)
    {
        if (exception == null) 
            throw new ArgumentNullException(nameof(exception));
            
        _promise.TrySetException(exception);
    }

    public void Cancel()
    {
        _promise.TrySetCanceled();
    }

    public void Dispose()
    {
        _owningPacketDispatcher.RemoveAwaitable(_packetIdentifier);
    }
}