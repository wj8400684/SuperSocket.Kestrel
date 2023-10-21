using BenchmarkDotNet.Running;
using Client.Benchmarks;

/// <summary>
/// 测试服务端
/// 服务端采用原生socket跟iocp
/// 客户端采用原生socket
/// </summary>
BenchmarkRunner.Run<ServerSendAndReceiveUsageClientIocpAsyncTest>();
Console.ReadKey();