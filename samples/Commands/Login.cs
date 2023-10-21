
using Core;
using Core.Packages;

namespace Commands;

[RpcCommand(CommandKey.Login)]
public sealed class Login : RpcAsyncRespIdentifierCommand<LoginPackage, LoginRespPackage>
{
    public Login(IPacketFactoryPool packetFactoryPool) : base(packetFactoryPool)
    {
    }

    protected override ValueTask<LoginRespPackage> ExecuteAsync(RpcSession session, LoginPackage packet, CancellationToken cancellationToken)
    {
        var response = CreateResponse();

        response.SuccessFul = true;
        response.Identifier = packet.Identifier;

        return ValueTask.FromResult(response);
        return new ValueTask<LoginRespPackage>(new LoginRespPackage
        {
            SuccessFul = true,
            Identifier = packet.Identifier,
        });
    }
}
