using Core;
using SuperSocket.Client;
using System.Diagnostics;
using System.Net;
using Client;
using SuperSocket.Console.Server;

var easyClient = new PipeNameEasyClient<RpcPackageBase, RpcPackageBase>(new RpcPipeLineFilter(), new RpcPackageEncode()).AsClient();

await easyClient.ConnectAsync(new NamedPipeEndPoint("namePipe"));

//await easyClient.ConnectAsync(new DnsEndPoint("127.0.0.1", 4040, System.Net.Sockets.AddressFamily.InterNetwork), CancellationToken.None);

var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

// while (await timer.WaitForNextTickAsync())
// {
//     await easyClient.SendAsync(new LoginPackage
//     {
//         Username = "sss",
//         Password = "password",
//     });
//
//     var reply = await easyClient.ReceiveAsync();
//
//     
// }

var count1 = 0;
var watch = new Stopwatch();
watch.Start();

Console.WriteLine($"开始执行");

do
{
    await easyClient.SendAsync(new LoginPackage
    {
        Username = "sss",
        Password = "password",
    });

    var reply = await easyClient.ReceiveAsync();
    count1++;
} while (watch.Elapsed.TotalSeconds < 60);

watch.Stop();

Console.WriteLine($"执行完成:{count1}");

//执行完成:1359644
//执行完成:1267578
//执行完成:1346857
//执行完成:1353495

//执行完成:1413667

//执行完成:1391411

Console.ReadKey();








Console.WriteLine("请输入发送次数，不输入默认为10w次按enter ");

var count = 1000 * 1000;

var input = Console.ReadLine();

if (!string.IsNullOrWhiteSpace(input)) 
    _ = int.TryParse(input, out count);

Console.WriteLine($"开始执行");

for (var i = 0; i < count; i++)
{
    await easyClient.SendAsync(new LoginPackage
    {
        Username = "sss",
        Password = "password",
    });

    var reply = await easyClient.ReceiveAsync();
}

watch.Stop();
Console.WriteLine($"执行完成{watch.ElapsedMilliseconds/1000}秒");

Console.ReadKey();