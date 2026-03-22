# MsgRpc

**MsgRpc** 是一个基于 .NET 的高性能、轻量级 RPC 框架。它通过 **Roslyn Source Generators** 消除了运行时反射，并深度集成 **MessagePack** 序列化，旨在为分布式系统提供接近原生调用的通信体验。



## 🌟 核心特性

* **⚡ 零反射 (Zero-Reflection)**：利用源码生成技术在编译期生成代理和分发逻辑，彻底告别运行时反射带来的性能损耗。
* **🚀 极致性能**：深度使用 `System.Buffers`、`ReadOnlySequence<T>` 和 `ArrayPool<byte>` 内存复用技术，最大限度减少 GC 压力。
* **📦 紧凑序列化**：集成 MessagePack 协议，相比 JSON 拥有更小的包体体积和更快的编解码速度。
* **🛡️ 弹性设计**：内置 `ReliableMsgRpcClient` 装饰器，支持指数退避（Exponential Backoff）和抖动（Jitter）重试策略。
* **🧬 异步优先**：全链路支持 `Task`/`ValueTask` 和 `CancellationToken`，适配高并发异步场景。
* **🛠️ 易于扩展**：采用插件式传输层设计，默认支持基于 `System.IO.Pipelines` 的高性能 TCP 实现。

---

## 🏗️ 架构概览

MsgRpc 的设计核心在于将“契约”与“实现”完全解耦：



1.  **Contract**: 用户定义带特性的接口。
2.  **Generator**: 编译时自动生成 DTO 结构体、客户端 Proxy 和服务端 Dispatcher。
3.  **Transport**: 负责字节流在网络中的可靠传输。
4.  **Builder**: 统一的 Fluent API 引导容器构建与服务注册。

---

## 🚀 快速上手

### 1. 定义服务契约
使用 `[MsgRpcService]` 标记接口，并为方法分配唯一的 `MethodId`。

```csharp
[MsgRpcService("WeatherService")]
public interface IWeatherService
{
    [MsgRpc(1)]
    Task<int> GetTemperatureAsync(string city);
}
```

### 2. 服务端：注册并启动
通过 `MsgRpcServerBuilder` 快速构建服务端环境。

```csharp
var server = new MsgRpcServerBuilder()
    .WithTransport<TcpServerTransport>()
    .WithLogging(log => log.AddConsole())
    .WithOptions(opt => opt.WithCompression(MessagePackCompression.Lz4BlockArray))
    // 'WithWeatherService' 是由 Source Generator 自动生成的扩展方法
    .WithWeatherService<WeatherImplementation>() 
    .Build();

await server.StartAsync(new IPEndPoint(IPAddress.Any, 6000));
```

### 3. 客户端：透明调用
像调用本地方法一样进行远程调用。

```csharp
var client = await new MsgRpcClientBuilder()
    .WithEndPoint(new IPEndPoint(IPAddress.Loopback, 6000))
    .WithRetryPolicy(maxRetries: 3)
    .BuildAsync();

// 'CreateWeatherService' 是生成的扩展方法
var service = client.CreateWeatherService();
int temp = await service.GetTemperatureAsync("Beijing");
```

---

## 📈 性能表现

在基准测试环境（Loopback, 100 并发任务）下，MsgRpc 展现了卓越的吞吐能力：

| 指标 | 测试结果 |
| :--- | :--- |
| **平均 QPS** | **24,000+** |
| **平均延迟** | **< 4ms** (100 并发下) |
| **内存分配** | 极低 (得益于 ArrayPool 复用) |

---

## 🛠️ 技术栈

* **Language**: C# 12+ / .NET 8/9+
* **Serialization**: [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)
* **IO Infrastructure**: [System.IO.Pipelines](https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines)
* **Dependency Injection**: Microsoft.Extensions.DependencyInjection

---

## 📄 开源协议

本项目采用 [MIT License](LICENSE) 开源协议。