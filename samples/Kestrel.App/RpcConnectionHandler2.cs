using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Bedrock.Framework.Protocols;
using Core;
using Core.Packages;
using Microsoft.AspNetCore.Connections;
using SuperSocket.ProtoBase;

namespace Kestrel.App;

internal sealed class LengthPrefixedProtocol(IPacketFactoryPool packetFactoryPool) :
    IMessageReader<RpcPackageBase>,
    IMessageWriter<RpcPackageBase>
{
    private const byte HeaderSize = sizeof(short);

    public bool TryParseMessage(in ReadOnlySequence<byte> input,
        ref SequencePosition consumed,
        ref SequencePosition examined,
        [UnscopedRef] out RpcPackageBase message)
    {
        var reader = new SequenceReader<byte>(input);
        if (!reader.TryReadLittleEndian(out short length) || reader.Remaining < length)
        {
            message = default;
            return false;
        }

        reader.TryRead(out var command);

        var packetFactory = packetFactoryPool.Get(command) ?? throw new ProtocolException($"un find{command}");

        message = packetFactory.Create();

        message.DecodeBody(ref reader, message);

        examined = consumed = input.Slice(HeaderSize + length).End;
        return true;
    }

    public void WriteMessage(RpcPackageBase message, IBufferWriter<byte> output)
    {
        //获取头部缓冲区
        var headSpan = output.GetSpan(HeaderSize);
        output.Advance(HeaderSize);

        //写入command
        var length = output.WriteLittleEndian((byte)message.Key);

        //编码
        length += message.Encode(output);

        //写入data长度
        BinaryPrimitives.WriteInt16LittleEndian(headSpan, (short)length);
    }
}

internal sealed class RpcConnectionHandler2(ILogger<RpcConnectionHandler> logger) : ConnectionHandler
{
    private readonly LengthPrefixedProtocol _protocol = new(new DefaultPacketFactoryPool());

    public override async Task OnConnectedAsync(ConnectionContext connection)
    {
        logger.LogInformation($"客户端连接：{connection.ConnectionId}-{connection.RemoteEndPoint}");

        var writer = connection.CreateWriter();
        var reader = connection.CreateReader();

        while (true)
        {
            try
            {
                var result = await reader.ReadAsync(_protocol);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                reader.Advance();
            }
            
            await writer.WriteAsync(_protocol, new LoginRespPackage
            {
                SuccessFul = true,
            });
        }
    }
}