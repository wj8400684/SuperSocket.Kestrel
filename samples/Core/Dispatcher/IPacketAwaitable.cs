namespace Core;

public interface IPacketAwaitable : IDisposable
{
    void Complete(RpcPackageBase packet);

    void Fail(Exception exception);

    void Cancel();
}