﻿using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using SuperSocket.Channel;
using SuperSocket.Kestrel.Internal;
using SuperSocket.ProtoBase;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace SuperSocket.Kestrel.Channel;

public sealed class KestrelPipeChannel<TPackageInfo> : ChannelBase<TPackageInfo>
{
    private bool _isAbort;
    private Task _readsTask;
    private IPipelineFilter<TPackageInfo> _pipelineFilter;

    private readonly ILogger _logger;
    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;
    private readonly ChannelOptions _options;
    private readonly ConnectionContext _connection;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationToken _connectionToken;
    private readonly KestrelObjectPipe<TPackageInfo> _packagePipe = new();

    public KestrelPipeChannel(ConnectionContext context,
        IPipelineFilter<TPackageInfo> pipelineFilter,
        ChannelOptions options)
    {
        _options = options;
        _logger = options.Logger;
        _connection = context;
        _reader = context.Transport.Input;
        _writer = context.Transport.Output;
        _pipelineFilter = pipelineFilter;
        _connectionToken = context.ConnectionClosed;
        LocalEndPoint = context.LocalEndPoint;
        RemoteEndPoint = context.RemoteEndPoint;
    }

    #region public

    public override void Start()
    {
        _readsTask = ReadPipeAsync(_reader);
        WaitHandleClosing();
    }

    public async override IAsyncEnumerable<TPackageInfo> RunAsync()
    {
        if (_readsTask == null)
            throw new Exception("The channel has not been started yet.");

        while (true)
        {
            var package = await _packagePipe.ReadAsync();

            if (package == null)
                yield break;

            yield return package;
        }
    }

    public override async ValueTask CloseAsync(CloseReason closeReason)
    {
        _isAbort = true;

        CloseReason = closeReason;

        _connection.Abort();

        await HandleClosing();
    }

    public override async ValueTask SendAsync(ReadOnlyMemory<byte> buffer)
    {
        try
        {
            await _sendLock.WaitAsync();
            UpdateLastActiveTime();
            WriteBuffer(_writer, buffer);
            await _writer.FlushAsync();
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
            await _sendLock.WaitAsync();
            UpdateLastActiveTime();
            WritePackageWithEncoder(_writer, packageEncoder, package);
            await _writer.FlushAsync();
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
            await _sendLock.WaitAsync();
            UpdateLastActiveTime();
            write(_writer);
            await _writer.FlushAsync();
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

    #region private

    private async void WaitHandleClosing()
    {
        await HandleClosing();
    }

    private async Task HandleClosing()
    {
        try
        {
            await _readsTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            OnError("Unhandled exception in the method PipeChannel.Run.", e);
        }
        finally
        {
            if (!IsClosed)
            {
                try
                {
                    Close();
                    OnClosed();
                }
                catch (Exception exc)
                {
                    if (!IsSocketIgnorableException(exc))
                        OnError("Unhandled exception in the method PipeChannel.Close.", exc);
                }
            }
        }
    }

    private void Close()
    {
        if (!_isAbort)
            _connection.Abort();
    }

    private static bool IsSocketIgnorableException(Exception e)
    {
        if (IsIgnorableException(e))
            return true;

        if (e is SocketException se && se.IsIgnorableSocketException())
            return true;

        return false;
    }

    private static bool IsIgnorableException(Exception e)
    {
        if (e is ObjectDisposedException or NullReferenceException)
            return true;

        if (e.InnerException != null)
            return IsIgnorableException(e.InnerException);

        return false;
    }

    private void ThrowChannelClosed()
    {
        if (IsClosed)
            throw new Exception("Channel is closed now, send is not allowed.");
    }

    private void UpdateLastActiveTime()
    {
        LastActiveTime = DateTimeOffset.Now;
    }

    private void WriteBuffer(PipeWriter writer, ReadOnlyMemory<byte> buffer)
    {
        ThrowChannelClosed();
        writer.Write(buffer.Span);
    }

    private void WritePackageWithEncoder<TPackage>(IBufferWriter<byte> writer, IPackageEncoder<TPackage> packageEncoder,
        TPackage package)
    {
        ThrowChannelClosed();
        packageEncoder.Encode(writer, package);
    }

    private async Task ReadPipeAsync(PipeReader reader)
    {
        while (!_connectionToken.IsCancellationRequested)
        {
            ReadResult result;

            try
            {
                result = await reader.ReadAsync();
            }
            catch (Exception e)
            {
                if (!IsSocketIgnorableException(e))
                {
                    OnError("Failed to read from the pipe", e);

                    CloseReason ??= _connectionToken.IsCancellationRequested
                        ? SuperSocket.Channel.CloseReason.RemoteClosing
                        : SuperSocket.Channel.CloseReason.SocketError;
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
                if (!buffer.IsEmpty)
                {
                    if (!ReaderBuffer(ref buffer, out consumed, out examined))
                        break;
                }

                if (completed)
                    break;

                UpdateLastActiveTime();
            }
            catch (Exception e)
            {
                OnError("Protocol error", e);
                // close the connection if get a protocol error
                CloseReason = SuperSocket.Channel.CloseReason.ProtocolError;
                Close();
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

    private bool ReaderBuffer(ref ReadOnlySequence<byte> buffer, out SequencePosition consumed,
        out SequencePosition examined)
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
                Close();
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

    #endregion
}