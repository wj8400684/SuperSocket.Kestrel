using SuperSocket.Channel;
using SuperSocket.ProtoBase;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using Microsoft.AspNetCore.Server.PipeLine;
using Microsoft.AspNetCore.Connections;
using SuperSocket.Kestrel.Internal;

namespace Kestrel.Abp.Server;

internal sealed class DuplexPipe : IDuplexPipe
{
    public DuplexPipe(PipeReader reader, PipeWriter writer)
    {
        Input = reader;
        Output = writer;
    }

    public PipeReader Input { get; }

    public PipeWriter Output { get; }

    public static DuplexPipePair CreateConnectionPair(PipeOptions inputOptions, PipeOptions outputOptions)
    {
        var input = new Pipe(inputOptions);
        var output = new Pipe(outputOptions);

        var transportToApplication = new DuplexPipe(output.Reader, input.Writer);

        var applicationToTransport = new DuplexPipe(input.Reader, output.Writer);

        return new DuplexPipePair(applicationToTransport, transportToApplication, input, output);
    }

    // This class exists to work around issues with value tuple on .NET Framework
    public readonly struct DuplexPipePair
    {
        public Pipe In { get; }

        public Pipe Out { get; }

        public IDuplexPipe Transport { get; }
        public IDuplexPipe Application { get; }

        public DuplexPipePair(IDuplexPipe transport, IDuplexPipe application, Pipe input, Pipe output)
        {
            In = input;
            Out = output;
            Transport = transport;
            Application = application;
        }
    }
}

public sealed class KestrelSocketConnection<TPackageInfo> :
    ChannelBase<TPackageInfo>,
    IChannel<TPackageInfo>,
    IChannel,
    IPipeChannel
{
    private static readonly int MinAllocBufferSize = 4096;

    private Socket _socket;
    private SocketSender _sender;
    private Task _readsTask;
    private Task _sendsTask;
    private bool _connectionClosed;

    private IPipelineFilter<TPackageInfo> _pipelineFilter;

    private volatile bool _socketDisposed;
    private volatile Exception _shutdownReason;

    private readonly ILogger _logger;
    private readonly PipeWriter _input;
    private readonly PipeReader _output;
    private readonly ChannelOptions _options;
    private readonly bool _waitForData;
    private readonly SocketReceiver _receiver;
    private readonly SocketSenderPool _socketSenderPool;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationToken _connectionToken;
    private readonly KestrelObjectPipe<TPackageInfo> _packagePipe = new();

    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;
    private readonly IDuplexPipe _application;
    private readonly IDuplexPipe _transport;
    private readonly object _shutdownLock = new();
    private readonly TaskCompletionSource _waitForConnectionClosedTcs = new();
    private readonly CancellationTokenSource _connectionClosedTokenSource = new();


    private CancellationTokenSource _cts = new();

    private readonly Pipe _in;
    private readonly Pipe _out;

    public KestrelSocketConnection(IPipelineFilter<TPackageInfo> pipelineFilter,
                                   ChannelOptions options,
                                   Socket socket,
                                   PipeScheduler socketScheduler,
                                   SocketSenderPool socketSenderPool,
                                   PipeOptions inputOptions,
                                   PipeOptions outputOptions,
                                   bool waitForData = true)
    {
        _options = options;
        _logger = options.Logger;
        _pipelineFilter = pipelineFilter;

        _socket = socket;
        _waitForData = waitForData;
        _socketSenderPool = socketSenderPool;

        var pair = DuplexPipe.CreateConnectionPair(inputOptions, outputOptions);

        _transport = pair.Transport;
        _application = pair.Application;

        _input = _application.Output;
        _output = _application.Input;

        _reader = pair.Transport.Input;
        _writer = pair.Transport.Output;

        _in = pair.In;
        _out = pair.Out;

        _receiver = new SocketReceiver(socketScheduler);
        _packagePipe = new KestrelObjectPipe<TPackageInfo>();

        RemoteEndPoint = socket.RemoteEndPoint;
        LocalEndPoint = socket.LocalEndPoint;
    }

    Pipe IPipeChannel.In => _in;

    Pipe IPipeChannel.Out => _out;

    IPipelineFilter IPipeChannel.PipelineFilter => _pipelineFilter;

    #region supersocket 

    /// <summary>
    /// 从socket中读取数据流然后写入pipiline
    /// </summary>
    /// <param name="writer"></param>
    /// <returns></returns>
    private async Task FillPipeAsync(PipeWriter writer)
    {
        var cts = _cts;

        int bytesRead;
        Memory<byte> memory;
        FlushResult flushResult;
        SocketOperationResult receiveResult;

        var bufferSize = _options.ReceiveBufferSize;
        if (bufferSize <= 0)
            bufferSize = 1024 * 4; //4k

        while (!cts.IsCancellationRequested)
        {
            try
            {
                memory = writer.GetMemory(bufferSize);

                if (_waitForData)
                {
                    // Wait for data before allocating a buffer.
                    var waitForDataResult = await _receiver.WaitForDataAsync(_socket);

                    if (waitForDataResult.HasError)
                        throw waitForDataResult.SocketError;
                }

                receiveResult = await _receiver.ReceiveAsync(_socket, memory);

                if (receiveResult.HasError)
                    throw receiveResult.SocketError;

                bytesRead = receiveResult.BytesTransferred;

                if (bytesRead == 0)
                {
                    if (!CloseReason.HasValue)
                        CloseReason = SuperSocket.Channel.CloseReason.RemoteClosing;

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

                    if (!CloseReason.HasValue)
                    {
                        CloseReason = cts.IsCancellationRequested
                            ? SuperSocket.Channel.CloseReason.LocalClosing : SuperSocket.Channel.CloseReason.SocketError;
                    }
                }
                else if (!CloseReason.HasValue)
                {
                    CloseReason = SuperSocket.Channel.CloseReason.RemoteClosing;
                }

                break;
            }

            // Make the data available to the PipeReader
            flushResult = await writer.FlushAsync();

            if (flushResult.IsCompleted)
                break;
        }

        // Signal to the reader that we're done writing
        await writer.CompleteAsync();
        // And don't allow writing data to outgoing pipeline
        await _out.Writer.CompleteAsync();
    }

    #endregion

    #region public

    public override void Start()
    {
        //// 从pipeline中读取数据然后发送至socket
        //_sendsTask = DoSendAsync();

        ////读取数据
        //_readsTask = ProcessReadsAsync();

        _sendsTask = DoSendAsyncWithSuperSocketAsync();

        _readsTask = DoReceiveAsyncWithSuperSocketAsync();

        WaitHandleClosing();
    }

    /// <summary>
    /// 发送数据至socket
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async ValueTask<int> SendOverIOAsync(ReadOnlySequence<byte> buffer,
                                                            CancellationToken cancellationToken)
    {
        _sender = _socketSenderPool.Rent();

        var transferResult = await _sender.SendAsync(_socket!, buffer).ConfigureAwait(false);

        if (transferResult.HasError)
        {
            if (IsConnectionResetError(transferResult.SocketError.SocketErrorCode))
                throw transferResult.SocketError;

            if (IsConnectionAbortError(transferResult.SocketError.SocketErrorCode))
                throw transferResult.SocketError;
        }

        _socketSenderPool.Return(_sender);

        _sender = null;

        return transferResult.BytesTransferred;
    }


    /// <summary>
    /// 循环体内直接发送
    /// </summary>
    /// <returns></returns>
    private async Task DoSendAsyncWithSuperSocketAsync()
    {
        var output = _out.Reader;
        var cts = _cts;

        while (true)
        {
            var result = await output.ReadAsync();

            var completed = result.IsCompleted;

            var buffer = result.Buffer;
            var end = buffer.End;

            if (!buffer.IsEmpty)
            {
                try
                {
                    await SendOverIOAsync(buffer, cts.Token);
                    //_sender = _socketSenderPool.Rent();

                    //var transferResult = await _sender.SendAsync(_socket, buffer);

                    //if (transferResult.HasError)
                    //{
                    //    if (IsConnectionResetError(transferResult.SocketError.SocketErrorCode))
                    //        throw transferResult.SocketError;

                    //    if (IsConnectionAbortError(transferResult.SocketError.SocketErrorCode))
                    //        throw transferResult.SocketError;
                    //}

                    //_socketSenderPool.Return(_sender);

                    //_sender = null;

                    LastActiveTime = DateTimeOffset.Now;
                }
                catch (Exception e)
                {
                    cts?.Cancel(false);

                    if (!IsIgnorableException(e))
                        OnError("Exception happened in SendAsync", e);

                    break;
                }
            }

            output.AdvanceTo(end);

            if (completed)
                break;

            //var completed = await ProcessOutputRead(output, cts);

            //if (completed)
            //{
            //    break;
            //}
        }

        output.Complete();
    }


    private async Task DoReceiveAsyncWithSuperSocketAsync()
    {
        var pipe = _in;

        //从socket中读取数据流然后写入pipiline
        Task writing = FillPipeAsync(pipe.Writer);

        //从pipiline读取数据流并且处理
        Task reading = ReadPipeAsync(pipe.Reader);

        await Task.WhenAll(reading, writing);
    }

    private async Task ProcessReadsAsync()
    {
        //从socket中读取数据流然后写写入pipeline
        var sendingTask = DoReceiveAsync();

        //从pipeline读取数据然后解析
        var readsTask = ReadPipeAsync(_reader);

        await Task.WhenAll(readsTask, sendingTask).ConfigureAwait(false);
    }

    /// <summary>
    /// 从pipeline读取数据并且发送至socket
    /// </summary>
    /// <param name="reader"></param>
    /// <param name="cts"></param>
    /// <returns></returns>
    private async ValueTask<bool> ProcessOutputRead(PipeReader reader, CancellationTokenSource cts)
    {
        var result = await _out.Reader.ReadAsync();

        var completed = result.IsCompleted;

        var buffer = result.Buffer;
        var end = buffer.End;

        if (!buffer.IsEmpty)
        {
            try
            {
                _sender = _socketSenderPool.Rent();

                var transferResult = await _sender.SendAsync(_socket, buffer);

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
                cts?.Cancel(false);

                if (!IsIgnorableException(e))
                    OnError("Exception happened in SendAsync", e);

                return true;
            }
        }

        _out.Reader.AdvanceTo(end);
        return completed;
    }

    public async override IAsyncEnumerable<TPackageInfo> RunAsync()
    {
        if (_readsTask == null)
            throw new Exception("The channel has not been started yet.");

        while (!_connectionToken.IsCancellationRequested)
        {
            var package = await _packagePipe.ReadAsync();

            if (package == null)
                yield break;

            yield return package;
        }

        ((IDisposable)_packagePipe).Dispose();
    }

    public override ValueTask CloseAsync(CloseReason closeReason)
    {
        _socketDisposed = true;

        CloseReason = closeReason;

        Shutdown(null);

        return ValueTask.CompletedTask;
    }

    public override async ValueTask SendAsync(ReadOnlyMemory<byte> buffer)
    {
        try
        {
            await _sendLock.WaitAsync().ConfigureAwait(false);
            var writer = _writer;
            WriteBuffer(writer, buffer);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public override async ValueTask SendAsync<TPackage>(IPackageEncoder<TPackage> packageEncoder, TPackage package)
    {
        try
        {
            await _sendLock.WaitAsync().ConfigureAwait(false);
            var writer = _writer;
            WritePackageWithEncoder(writer, packageEncoder, package);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public override async ValueTask SendAsync(Action<PipeWriter> write)
    {
        try
        {
            await _sendLock.WaitAsync().ConfigureAwait(false);
            var writer = _writer;
            write(_writer);
            await writer.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public override ValueTask DetachAsync()
    {
        throw new NotImplementedException();
    }

    #endregion

    #region 读取写入

    /// <summary>
    /// 从socket中读取数据流然后写入pipeline
    /// </summary>
    /// <returns></returns>
    private async Task DoReceiveAsync()
    {
        Exception error = null;

        try
        {
            while (true)
            {
                if (_waitForData)
                {
                    // Wait for data before allocating a buffer.
                    var waitForDataResult = await _receiver.WaitForDataAsync(_socket);

                    if (!IsNormalCompletion(waitForDataResult))
                    {
                        break;
                    }
                }

                // Ensure we have some reasonable amount of buffer space
                var buffer = _input.GetMemory(MinAllocBufferSize);

                var receiveResult = await _receiver.ReceiveAsync(_socket, buffer);

                if (!IsNormalCompletion(receiveResult))
                {
                    break;
                }

                var bytesReceived = receiveResult.BytesTransferred;

                if (bytesReceived == 0)
                {
                    // FIN
                    //SocketsLog.ConnectionReadFin(_logger, this);
                    break;
                }

                _input.Advance(bytesReceived);

                var flushTask = _input.FlushAsync();

                var paused = !flushTask.IsCompleted;

                if (paused)
                {
                    //SocketsLog.ConnectionPause(_logger, this);
                }

                var result = await flushTask;

                if (paused)
                {
                    //SocketsLog.ConnectionResume(_logger, this);
                }

                if (result.IsCompleted || result.IsCanceled)
                {
                    // Pipe consumer is shut down, do we stop writing
                    break;
                }

                bool IsNormalCompletion(SocketOperationResult result)
                {
                    if (!result.HasError)
                    {
                        return true;
                    }

                    if (IsConnectionResetError(result.SocketError.SocketErrorCode))
                    {
                        // This could be ignored if _shutdownReason is already set.
                        var ex = result.SocketError;
                        error = new ConnectionResetException(ex.Message, ex);

                        // There's still a small chance that both DoReceive() and DoSend() can log the same connection reset.
                        // Both logs will have the same ConnectionId. I don't think it's worthwhile to lock just to avoid this.
                        if (!_socketDisposed)
                        {
                            //SocketsLog.ConnectionReset(_logger, this);
                        }

                        return false;
                    }

                    if (IsConnectionAbortError(result.SocketError.SocketErrorCode))
                    {
                        // This exception should always be ignored because _shutdownReason should be set.
                        error = result.SocketError;

                        if (!_socketDisposed)
                        {
                            // This is unexpected if the socket hasn't been disposed yet.
                            //SocketsLog.ConnectionError(_logger, this, error);
                        }

                        return false;
                    }

                    // This is unexpected.
                    error = result.SocketError;
                    //SocketsLog.ConnectionError(_logger, this, error);

                    return false;
                }
            }
        }
        catch (ObjectDisposedException ex)
        {
            // This exception should always be ignored because _shutdownReason should be set.
            error = ex;

            if (!_socketDisposed)
            {
                // This is unexpected if the socket hasn't been disposed yet.
                //SocketsLog.ConnectionError(_logger, this, error);
            }
        }
        catch (Exception ex)
        {
            // This is unexpected.
            error = ex;
            //SocketsLog.ConnectionError(_logger, this, error);
        }
        finally
        {
            // If Shutdown() has already been called, assume that was the reason ProcessReceives() exited.
            _input.Complete(_shutdownReason ?? error);

            FireConnectionClosed();

            await _waitForConnectionClosedTcs.Task;
        }
    }

    /// <summary>
    /// 从pipeline中读取数据然后发送至socket
    /// </summary>
    /// <returns></returns>
    private async Task DoSendAsync()
    {
        Exception shutdownReason = null;
        Exception unexpectedError = null;

        try
        {
            while (true)
            {
                var result = await _output.ReadAsync();

                if (result.IsCanceled)
                {
                    break;
                }
                var buffer = result.Buffer;

                if (!buffer.IsEmpty)
                {
                    _sender = _socketSenderPool.Rent();
                    var transferResult = await _sender.SendAsync(_socket, buffer);

                    if (transferResult.HasError)
                    {
                        if (IsConnectionResetError(transferResult.SocketError.SocketErrorCode))
                        {
                            var ex = transferResult.SocketError;
                            shutdownReason = new ConnectionResetException(ex.Message, ex);
                            //SocketsLog.ConnectionReset(_logger, this);

                            break;
                        }

                        if (IsConnectionAbortError(transferResult.SocketError.SocketErrorCode))
                        {
                            shutdownReason = transferResult.SocketError;

                            break;
                        }

                        unexpectedError = shutdownReason = transferResult.SocketError;
                    }

                    // We don't return to the pool if there was an exception, and
                    // we keep the _sender assigned so that we can dispose it in StartAsync.
                    _socketSenderPool.Return(_sender);
                    _sender = null;
                }

                _output.AdvanceTo(buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (ObjectDisposedException ex)
        {
            // This should always be ignored since Shutdown() must have already been called by Abort().
            shutdownReason = ex;
        }
        catch (Exception ex)
        {
            shutdownReason = ex;
            unexpectedError = ex;
            //SocketsLog.ConnectionError(_logger, this, unexpectedError);
        }
        finally
        {
            Shutdown(shutdownReason);

            // Complete the output after disposing the socket
            _output.Complete(unexpectedError);

            // Cancel any pending flushes so that the input loop is un-paused
            _input.CancelPendingFlush();
        }
    }

    private void FireConnectionClosed()
    {
        // Guard against scheduling this multiple times
        if (_connectionClosed)
        {
            return;
        }

        _connectionClosed = true;

        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.CancelConnectionClosedToken();

            state._waitForConnectionClosedTcs.TrySetResult();
        },
        this,
        preferLocal: false);
    }

    private void CancelConnectionClosedToken()
    {
        try
        {
            _connectionClosedTokenSource.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogError(0, ex, $"Unexpected exception in {nameof(KestrelSocketConnection<TPackageInfo>)}.{nameof(CancelConnectionClosedToken)}.");
        }
    }

    private void Shutdown(Exception shutdownReason)
    {
        lock (_shutdownLock)
        {
            if (_socketDisposed)
            {
                return;
            }

            // Make sure to close the connection only after the _aborted flag is set.
            // Without this, the RequestsCanBeAbortedMidRead test will sometimes fail when
            // a BadHttpRequestException is thrown instead of a TaskCanceledException.
            _socketDisposed = true;

            // shutdownReason should only be null if the output was completed gracefully, so no one should ever
            // ever observe the nondescript ConnectionAbortedException except for connection middleware attempting
            // to half close the connection which is currently unsupported.
            _shutdownReason = shutdownReason ?? new ConnectionAbortedException("The Socket transport's send loop completed gracefully.");

            _logger.LogError(_shutdownReason, _shutdownReason.Message);

            try
            {
                // Try to gracefully close the socket even for aborts to match libuv behavior.
                _socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
                // Ignore any errors from Socket.Shutdown() since we're tearing down the connection anyway.
            }

            _socket.Dispose();
        }
    }

    #endregion

    #region private

    private async void WaitHandleClosing()
    {
        await HandleClosing().ConfigureAwait(false);
    }

    private async Task HandleClosing()
    {
        Exception ex = null;

        try
        {
            await Task.WhenAll(_readsTask, _sendsTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            ex = e;
            OnError("Unhandled exception in the method PipeChannel.Run.", e);
        }
        finally
        {
            if (!IsClosed)
            {
                try
                {
                    Shutdown(ex);
                    OnClosed();
                }
                catch (Exception exc)
                {
                    if (!IsIgnorableException(exc))
                        OnError("Unhandled exception in the method PipeChannel.Close.", exc);
                }
            }
        }
    }

    private bool IsIgnorableException(Exception e)
    {
        if (e is ObjectDisposedException || e is NullReferenceException)
            return true;

        if (e.InnerException != null)
            return IsIgnorableException(e.InnerException);

        if (e is SocketException ex && ex.IsIgnorableSocketException())
            return true;

        return false;
    }

    private void CheckChannelOpen()
    {
        if (IsClosed)
            throw new Exception("Channel is closed now, send is not allowed.");
    }

    private void WriteBuffer(PipeWriter writer, ReadOnlyMemory<byte> buffer)
    {
        CheckChannelOpen();
        writer.Write(buffer.Span);
    }

    private void WritePackageWithEncoder<TPackage>(IBufferWriter<byte> writer, IPackageEncoder<TPackage> packageEncoder, TPackage package)
    {
        CheckChannelOpen();
        packageEncoder.Encode(writer, package);
    }

    /// <summary>
    /// 从pipeline读取数据然后解析
    /// </summary>
    /// <param name="reader"></param>
    /// <returns></returns>
    private async Task ReadPipeAsync(PipeReader reader)
    {
        while (!_connectionToken.IsCancellationRequested)
        {
            ReadResult result;

            try
            {
                result = await reader.ReadAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (!IsIgnorableException(e))
                {
                    OnError("Failed to read from the pipe", e);

                    if (!CloseReason.HasValue)
                    {
                        CloseReason = _connectionToken.IsCancellationRequested
                            ? SuperSocket.Channel.CloseReason.RemoteClosing : SuperSocket.Channel.CloseReason.SocketError;
                    }
                }
                else if (!CloseReason.HasValue)
                {
                    CloseReason = SuperSocket.Channel.CloseReason.Unknown;
                }

                break;
            }

            var buffer = result.Buffer;

            SequencePosition consumed = buffer.Start;
            SequencePosition examined = buffer.End;

            if (result.IsCanceled)
                break;

            var completed = result.IsCompleted;

            try
            {
                if (buffer.Length > 0)
                {
                    if (!ReaderBuffer(ref buffer, out consumed, out examined))
                    {
                        completed = true;
                        break;
                    }
                }

                if (completed)
                    break;
            }
            catch (Exception e)
            {
                OnError("Protocol error", e);
                // close the connection if get a protocol error
                CloseReason = SuperSocket.Channel.CloseReason.ProtocolError;
                Shutdown(e);
                break;
            }
            finally
            {
                reader.AdvanceTo(consumed, examined);
            }
        }

        reader.Complete();
        WriteEOFPackage();
    }

    private void WriteEOFPackage()
    {
        _packagePipe.Write(default);
    }

    private bool ReaderBuffer(ref ReadOnlySequence<byte> buffer, out SequencePosition consumed, out SequencePosition examined)
    {
        consumed = buffer.Start;
        examined = buffer.End;

        var bytesConsumedTotal = 0L;

        var maxPackageLength = _options.MaxPackageLength;

        var seqReader = new SequenceReader<byte>(buffer);

        while (true)
        {
            _connectionToken.ThrowIfCancellationRequested();

            var currentPipelineFilter = _pipelineFilter;
            var filterSwitched = false;

            var packageInfo = currentPipelineFilter.Filter(ref seqReader);

            var nextFilter = currentPipelineFilter.NextFilter;

            if (nextFilter != null)
            {
                nextFilter.Context = currentPipelineFilter.Context; // pass through the context
                _pipelineFilter = nextFilter;
                filterSwitched = true;
            }

            var bytesConsumed = seqReader.Consumed;
            bytesConsumedTotal += bytesConsumed;

            var len = bytesConsumed;

            // nothing has been consumed, need more data
            if (len == 0)
                len = seqReader.Length;

            if (maxPackageLength > 0 && len > maxPackageLength)
            {
                OnError($"Package cannot be larger than {maxPackageLength}.");
                CloseReason = SuperSocket.Channel.CloseReason.ProtocolError;
                // close the the connection directly
                Shutdown(null);
                return false;
            }

            if (packageInfo == null)
            {
                // the current pipeline filter needs more data to process
                if (!filterSwitched)
                {
                    // set consumed position and then continue to receive...
                    consumed = buffer.GetPosition(bytesConsumedTotal);
                    return true;
                }

                // we should reset the previous pipeline filter after switch
                currentPipelineFilter.Reset();
            }
            else
            {
                // reset the pipeline filter after we parse one full package
                currentPipelineFilter.Reset();
                _packagePipe.Write(packageInfo);
            }

            if (seqReader.End) // no more data
            {
                examined = consumed = buffer.End;
                return true;
            }

            if (bytesConsumed > 0)
                seqReader = new SequenceReader<byte>(seqReader.Sequence.Slice(bytesConsumed));
        }
    }

    private void OnError(string message, Exception e = null)
    {
        if (e != null)
            _logger?.LogError(e, message);
        else
            _logger?.LogError(message);
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

    #endregion
}
