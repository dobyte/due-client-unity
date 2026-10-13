# due-client-unity

### 1.介绍

due是一款基于Go语言开发的轻量级、高性能分布式游戏服务器框架。
其中，模块设计方面借鉴了[kratos](https://github.com/go-kratos/kratos)的模块设计思路，旨在为游戏服务器开发提供完善、高效、标准化的解决方案。
框架自创建至今已在多个企业级游戏项目中上线实践过，稳定性有充分的保障。

本项目是 due 游戏框架的 Unity 客户端，方便开发者使用 Unity 对接 due 网关进行游戏开发与联调。

### 2.优势

* 💡 简单性：架构简单，源码简洁易理解。
* 🚠 便捷性：仅暴露必要的调用接口，减轻开发者的心智负担。
* 🚀 高性能：与 due 框架原生二进制协议完全一致，传输零冗余。
* ✈️ 多协议：同时提供 tcp、ws 两种传输实现，可按需自由切换。
* 🎟️ 扩展性：网络层（INetwork）、编解码器（IEncoding）均为接口设计，方便扩展自有实现。
* ⚖️ 稳定性：协议与 due 官方网关严格对齐，已在真实业务中联调验证。
* 🧵 主线程：网络事件回调统一派发到 Unity 主线程，可直接操作 UI 与场景。

### 3.功能

* 网关：支持 tcp、ws 两种协议的客户端接入。
* 协议：支持 json 编解码（基于 UnityEngine.JsonUtility），可扩展自定义编解码器。
* 心跳：内置心跳保活，支持接收服务器心跳附带的服务器时间。
* 网络：网络层接口化设计（INetwork），内置 tcp（Tcp）与 ws（Ws）两种实现。
* 打包：打包器（Packer）支持自定义字节序、路由字节数、序列号字节数、消息体上限。
* 线程：网络事件回调自动派发到 Unity 主线程，避免跨线程操作报错。

### 4.安装

将 `Runtime` 目录下的所有文件复制到 Unity 项目的 `Assets` 目录下即可使用。
其中包含 Assembly Definition（`Due.asmdef`），会自动生成独立的 `Due` 程序集。

### 5.使用

```csharp
using Due;
using Due.Network;
using UnityEngine;

public class Demo : MonoBehaviour
{
    private Client client;

    private void Start()
    {
        // 创建打包器
        Packer packer = new Packer();

        // 创建客户端：TCP 传输
        // client = new Client(new Tcp(packer), packer);

        // 创建客户端：WebSocket 传输
        client = new Client(new Ws(packer), packer);

        // 监听连接
        client.OnConnect(c =>
        {
            Debug.Log("连接成功");
            c.Send(1, 1, new ChatMessage { Content = "hello server" });
        });

        // 监听断开连接
        client.OnDisconnect(c =>
        {
            Debug.Log("连接断开");

            // 重新连接
            c.Connect("ws://127.0.0.1:3553");
        });

        // 监听接收消息
        client.OnReceive((c, message) =>
        {
            var data = message.Data?.GetObject<ChatMessage>();
            Debug.Log($"收到消息; route: {message.Route}, seq: {message.Seq}, content: {data?.Content}");
        });

        // 监听服务器心跳附带的服务器时间
        client.OnHeartbeat((c, millisecond) =>
        {
            if (millisecond != null)
            {
                Debug.Log($"服务器心跳时间戳: {millisecond}ms");
            }
        });

        // 建立连接
        client.Connect("ws://127.0.0.1:3553");
    }

    private void OnDestroy()
    {
        client?.Disconnect();
    }
}

// 消息体：JsonUtility 仅序列化 public 字段，需标记 [Serializable]
[System.Serializable]
public class ChatMessage
{
    public string Content;
}
```

> 注意：`JsonUtility` 仅支持标记了 `[System.Serializable]` 的类/结构体（public 字段），不支持字典、匿名对象等。如需更灵活的 JSON 支持，可实现 `IEncoding` 接口并注入 Newtonsoft.Json 等实现。

### 6.打包器配置

打包器（Packer）用于消息的打包与解包，可通过构造函数自定义协议参数：

```csharp
// 字节序、路由字节数、序列号字节数、编解码器
Packer packer = new Packer(
    byteOrder: ByteOrder.BigEndian, // 字节序，默认为 BigEndian
    routeBytes: 2,                  // 路由字节数，可选 1 | 2 | 4，默认为 2
    seqBytes: 2,                    // 序列号字节数，可选 1 | 2 | 4，默认为 2
    encoding: new Json()            // 编解码器，默认为 Json
);
```

> 注意：客户端的打包器配置需与服务器端 `etc.packet` 配置保持一致，否则无法正常通信。

### 7.网络层切换

网络层（INetwork）负责底层连接的建立与数据收发，目前内置两种实现，构造客户端时按需注入即可：

```csharp
// TCP 传输（对应服务器 tcp 网关）
Client client = new Client(new Tcp(packer), packer);
client.Connect("127.0.0.1:3553");

// WebSocket 传输（对应服务器 ws 网关）
Client client = new Client(new Ws(packer), packer);
client.Connect("ws://127.0.0.1:3553");
```

> 注意：WebSocket 依赖 `System.Net.WebSockets`，需将项目 API Compatibility Level 设置为 .NET Standard 2.1 或 .NET Framework 4.x；WebGL 平台需自行实现基于原生 WebSocket 的 `INetwork` 实现。

## License

[MIT](https://tldrlegal.com/license/mit-license)
