using Core;
using Microsoft.Extensions.Logging;
using SuperSocket.Channel;
using SuperSocket.ProtoBase;
using SuperSocket.Server;
using System.Net;

namespace Commands;

public sealed class RpcSession : AppSession
{
    private readonly IPackageEncoder<RpcPackageBase> _encoder;
    private readonly CancellationTokenSource _connectionTokenSource;
    private readonly PacketDispatcher _packetDispatcher = new();
    private readonly PacketIdentifierProvider _packetIdentifierProvider = new();

    public RpcSession(
        IPackageEncoder<RpcPackageBase> packageEncoder)
    {
        _encoder = packageEncoder;
        _connectionTokenSource = new CancellationTokenSource();
    }

    #region 属性

    /// <summary>
    /// 远程地址
    /// </summary>
    internal string RemoteAddress { get; private set; } = null!;

    /// <summary>
    /// 连接token
    /// </summary>
    internal CancellationToken ConnectionToken => _connectionTokenSource.Token;

    #endregion

    /// <summary>
    /// 客户端连接
    /// </summary>
    /// <returns></returns>
    protected override ValueTask OnSessionConnectedAsync()
    {
        RemoteAddress = ((IPEndPoint)RemoteEndPoint).Address.ToString();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 客户端断开
    /// </summary>
    /// <param name="e"></param>
    /// <returns></returns>
    protected override ValueTask OnSessionClosedAsync(CloseEventArgs e)
    {
        _packetIdentifierProvider.Reset();
        _connectionTokenSource.Cancel();
        _connectionTokenSource.Dispose();
        _packetDispatcher.CancelAll();
        _packetDispatcher.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 获取响应包
    /// </summary>
    /// <typeparam name="TResponsePacket"></typeparam>
    /// <param name="package"></param>
    /// <param name="responseTimeout"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    internal ValueTask<TResponsePacket> GetResponsePacketAsync<TResponsePacket>(
         RpcPackageWithIdentifier package,
         TimeSpan responseTimeout,
         CancellationToken cancellationToken) where TResponsePacket : RpcRespPackageWithIdentifier
    {
        using var timeOut = new CancellationTokenSource(responseTimeout);
        return GetResponsePacketAsync<TResponsePacket>(package, ConnectionToken, cancellationToken, timeOut.Token);
    }

    /// <summary>
    /// 获取响应包
    /// </summary>
    /// <typeparam name="TResponsePacket"></typeparam>
    /// <param name="package"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    internal ValueTask<TResponsePacket> GetResponsePacketAsync<TResponsePacket>(
         RpcPackageWithIdentifier package,
         CancellationToken cancellationToken) where TResponsePacket : RpcRespPackageWithIdentifier
    {
        return GetResponsePacketAsync<TResponsePacket>(package, ConnectionToken, cancellationToken);
    }

    /// <summary>
    /// 获取响应包
    /// </summary>
    /// <typeparam name="TResponsePacket"></typeparam>
    /// <param name="package"></param>
    /// <param name="tokens"></param>
    /// <returns></returns>
    internal async ValueTask<TResponsePacket> GetResponsePacketAsync<TResponsePacket>(
        RpcPackageWithIdentifier package,
        params CancellationToken[] tokens) where TResponsePacket : RpcRespPackageWithIdentifier
    {
        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(tokens);

        package.Identifier = _packetIdentifierProvider.GetNextPacketIdentifier();

        using var packetAwaitable = _packetDispatcher.AddAwaitable<TResponsePacket>(package.Identifier);

        this.LogDebug($"[{RemoteAddress}]: commandKey= {package.Key};Identifier= {package.Identifier} WaitAsync");

        try
        {
            //发送转发封包
            await SendPacketAsync(package);
        }
        catch (Exception e)
        {
            packetAwaitable.Fail(e);
            this.LogError(e, $"[{RemoteAddress}]: commandKey= {package.Key};Identifier= {package.Identifier} WaitAsync 发送封包抛出一个异常");
        }

        try
        {
            //等待封包结果
            return await packetAwaitable.WaitAsync(tokenSource.Token);
        }
        catch (Exception e)
        {
            if (e is TimeoutException)
                this.LogError($"[{RemoteAddress}]: commandKey= {package.Key};Identifier= {package.Identifier} WaitAsync Timeout");

            throw;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="package"></param>
    /// <returns></returns>
    internal ValueTask TryDispatchAsync(RpcRespPackageWithIdentifier package)
    {
        var result = _packetDispatcher.TryDispatch(package);

        this.LogDebug($"[{RemoteAddress}]: commandKey= {package.Key};Identifier= {package.Identifier} TryDispatch result= {result}");

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 发送包
    /// </summary>
    /// <param name="packet"></param>
    /// <returns></returns>
    internal ValueTask SendPacketAsync(RpcPackageBase packet)
    {
        if (Channel.IsClosed)
            return ValueTask.CompletedTask;

        return Channel.SendAsync(_encoder, packet);
    }
}