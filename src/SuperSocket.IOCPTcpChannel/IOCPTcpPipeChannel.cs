using SuperSocket.Channel;
using SuperSocket.ProtoBase;
using System;
using System.IO.Pipelines;
using System.Net.Sockets;
using Microsoft.AspNetCore.Server.PipeLine;

namespace SuperSocket.IOCPTcpChannel;

public class IOCPTcpPipeChannel<TPackageInfo> : TcpPipeChannel<TPackageInfo>
{
    private SocketSender? _sender;
    private readonly Socket _socket;
    private readonly bool _waitForData;
    private readonly SocketReceiver _receiver;
    private readonly SocketSenderPool _socketSenderPool;

    public IOCPTcpPipeChannel(Socket socket,
                              IPipelineFilter<TPackageInfo> pipelineFilter,
                              ChannelOptions options,
                              SocketSenderPool socketSenderPool,
                              PipeScheduler? socketScheduler = default,
                              bool waitForData = true) :
        base(socket, pipelineFilter, options)
    {
        socketScheduler ??= PipeScheduler.ThreadPool;

        _socket = socket;
        _waitForData = waitForData;
        _socketSenderPool = socketSenderPool;
        _receiver = new SocketReceiver(socketScheduler);
    }

    public override async ValueTask CloseAsync(CloseReason closeReason)
    {
        //Close Socket
        base.Close();

        //Wait for the read task and send the task to end
        await base.CloseAsync(closeReason);
    }

    /// <summary>
    /// Read data from the pipeline and send it to the socket
    /// </summary>
    /// <returns></returns>
    protected override async Task ProcessSends()
    {
        var output = Out.Reader;

        while (true)
        {
            var result = await output.ReadAsync().ConfigureAwait(false);

            var completed = result.IsCompleted;

            var buffer = result.Buffer;
            var end = buffer.End;

            if (!buffer.IsEmpty)
            {
                try
                {
                    _sender = _socketSenderPool.Rent();

                    var transferResult = await _sender.SendAsync(_socket, buffer).ConfigureAwait(false);

                    if (transferResult.HasError)
                    {
                        if (IsConnectionResetError(transferResult.SocketError.SocketErrorCode))
                            throw transferResult.SocketError;

                        if (IsConnectionAbortError(transferResult.SocketError.SocketErrorCode))
                            throw transferResult.SocketError;
                    }

                    _socketSenderPool.Return(_sender);

                    _sender = null;

                    LastActiveTime = DateTimeOffset.Now;
                }
                catch (Exception e)
                {
                    Cancel();

                    if (!IsIgnorableException(e))
                        OnError("Exception happened in SendAsync", e);

                    break;
                }
            }

            output.AdvanceTo(end);

            if (completed)
                break;
        }

        output.Complete();
    }

    /// <summary>
    /// Read the data stream from the socket and write it to the pipeline
    /// </summary>
    /// <param name="writer"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected override async Task FillPipeAsync(PipeWriter writer, CancellationToken cancellationToken)
    {
        var socket = _socket;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var bufferSize = Options.ReceiveBufferSize;

                if (bufferSize <= 0)
                    bufferSize = 1024 * 4; //4k

                var memory = writer.GetMemory(bufferSize);

                if (_waitForData)
                {
                    // Wait for data before allocating a buffer.
                    var waitForDataResult = await _receiver.WaitForDataAsync(socket).ConfigureAwait(false);

                    if (waitForDataResult.HasError)
                        throw waitForDataResult.SocketError;
                }

                var receiveResult = await _receiver.ReceiveAsync(socket, memory).ConfigureAwait(false);

                if (receiveResult.HasError)
                    throw receiveResult.SocketError;

                var bytesRead = receiveResult.BytesTransferred;

                if (bytesRead == 0)
                {
                    CloseReason ??= Channel.CloseReason.RemoteClosing;

                    break;
                }

                LastActiveTime = DateTimeOffset.Now;

                // Tell the PipeWriter how much was read
                writer.Advance(bytesRead);
            }
            catch (Exception e)
            {
                if (!IsIgnorableException(e))
                {
                    if (e is not OperationCanceledException)
                        OnError("Exception happened in ReceiveAsync", e);

                    CloseReason ??= cancellationToken.IsCancellationRequested
                        ? Channel.CloseReason.LocalClosing
                        : Channel.CloseReason.SocketError;
                }
                else if (!CloseReason.HasValue)
                {
                    CloseReason = Channel.CloseReason.RemoteClosing;
                }

                break;
            }

            // Make the data available to the PipeReader
            var result = await writer.FlushAsync().ConfigureAwait(false);

            if (result.IsCompleted)
                break;
        }

        // Signal to the reader that we're done writing
        await writer.CompleteAsync().ConfigureAwait(false);
        // And don't allow writing data to outgoing pipeline
        await Out.Writer.CompleteAsync().ConfigureAwait(false);
    }

    protected override void OnClosed()
    {
        _sender?.Dispose();
        _receiver.Dispose();
        base.OnClosed();
    }

    private static bool IsConnectionResetError(SocketError errorCode)
    {
        return errorCode == SocketError.ConnectionReset ||
               errorCode == SocketError.Shutdown ||
               (errorCode == SocketError.ConnectionAborted && OperatingSystem.IsWindows());
    }

    private static bool IsConnectionAbortError(SocketError errorCode)
    {
        // Calling Dispose after ReceiveAsync can cause an "InvalidArgument" error on *nix.
        return errorCode == SocketError.OperationAborted ||
               errorCode == SocketError.Interrupted ||
               (errorCode == SocketError.InvalidArgument && !OperatingSystem.IsWindows());
    }
}