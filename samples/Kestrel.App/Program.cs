using Kestrel.App;
using Microsoft.AspNetCore.Connections;

var builder = WebApplication.CreateSlimBuilder(args);

var section = builder.Configuration.GetSection("Kestrel");

builder.WebHost.ConfigureKestrel(options =>
{
    options.Configure(section).Endpoint("Echo", endpoint => endpoint.ListenOptions.UseConnectionHandler<RpcConnectionHandler2>());
});

var app = builder.Build();

app.Run();
