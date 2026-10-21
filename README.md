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
* 安全：支持 due 会话安全（security），握手协商会话密钥并对业务消息加解密。
* 线程：网络事件回调自动派发到 Unity 主线程，避免跨线程操作报错。

### 4.安装

将 `Runtime` 目录下的所有文件复制到 Unity 项目的 `Assets` 目录下即可使用。
其中包含 Assembly Definition（`Due.asmdef`），会自动生成独立的 `Due` 程序集。

仓库中的 `Tests` 目录为单元测试（覆盖安全模块与打包器），需连同 `Runtime` 一起放入 `Assets`，然后通过 Unity 的 Test Runner（Window > General > Test Runner）运行。

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

            // 重连建议重新创建 Client（及 Tcp/Ws）实例，详见「9.6 断线重连」
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

### 8.会话安全（security）

due 支持会话安全：客户端与服务端先完成握手，协商出双向 AEAD 会话密钥，之后所有业务消息的 payload 都会被加密，路由与序列号通过 AAD 绑定，防止篡改。

服务端启用会话安全（需配置签名私钥，客户端使用对应公钥验签）：

```go
gate := gate.NewGate(
    gate.WithServer(tcp.NewServer()),
    gate.WithSessionSecurity(ecc.NewSigner(ecc.WithSignerPrivateKey("keys/gate.pem"))),
)
```

Unity 客户端启用会话安全：

```csharp
Packer packer = new Packer();

SecurityOptions security = new SecurityOptions
{
    // 服务端公钥：可以是 PEM 文件路径，也可以是 PEM 内容
    Verifier = ServerVerifier.Ecdsa("keys/gate.pub.pem"),
    // 握手超时时间（毫秒），默认为 5000
    HandshakeTimeout = 5000,
};

Client client = new Client(new Ws(packer), packer, security);

client.OnConnect(c =>
{
    // 握手完成后才会触发 OnConnect，此时可以正常收发消息
    c.Send(1, 1, new ChatMessage { Content = "hello server" });
});

client.Connect("ws://127.0.0.1:3553");
```

> 注意：
> * 握手使用 X25519 协商临时密钥，通过 HKDF-SHA256 派生双向密钥；客户端会用配置的公钥校验服务端 ServerHello 签名。
> * 内置 AEAD 套件为 AES-256-GCM（套件编号 1，纯托管实现，不依赖平台自带的 `AesGcm`）；客户端默认只提供该套件，需要与服务端启用的套件有交集。
> * 只有握手成功后才会触发 `OnConnect`；握手完成前调用 `Send` 会返回 `false`。心跳帧保持明文，不参与会话加密。
> * 每条连接都会重新握手，重连无需额外处理；握手失败或超时会自动断开连接。
> * 服务端使用 RSA 签名时，改用 `ServerVerifier.Rsa("keys/gate.pub.pem")`（默认 PSS 填充，如需 PKCS#1 v1.5 传入 `pss: false`）。
> * 如需自定义验签逻辑（例如使用 HMAC 测试签名），可直接传入 `HandshakeVerifier` 委托；如需扩展 AEAD 算法，可实现 `IAeadSuite` 并通过 `SecurityOptions.Suites` 注入。
> * 签名算法默认 SHA-256，需与服务端 `etc.crypto.ecc.signer.hash` 配置一致；ECDSA 签名的分隔符默认为空格，与服务端 `etc.crypto.ecc.signer.delimiter` 配置一致。

### 9.接入说明

本章按“从零接入到联调通过”的顺序整理接入步骤、配置对照与常见问题。

#### 9.1 环境要求

* Unity 2020.2 及以上（项目使用 C# 8 语法），推荐 Unity 2021.3 LTS。
* API Compatibility Level 建议设置为 `.NET Standard 2.1`（WebSocket 也要求该级别或 `.NET Framework 4.x`）。
* 桌面、移动平台（Mono / IL2CPP）均可使用；WebGL 受运行时限制，`System.Net.WebSockets` 与部分加密 API 不可用，需自行实现 `INetwork` 并验证验签能力。
* 会话安全不依赖平台自带的 `AesGcm`，使用内置的纯托管 AES-256-GCM；ECDSA / RSA 验签依赖 `System.Security.Cryptography.ECDsa` / `RSA`。

#### 9.2 接入步骤

1. 导入 SDK：将 `Runtime` 目录整体复制到 Unity 工程的 `Assets` 下，保留 `Due.asmdef`。
2. 对齐协议参数：确认服务端 `etc.packet` 的 `byteOrder`、`routeBytes`、`seqBytes`，用相同参数构造 `Packer`。
3. 对齐编解码器：客户端 `IEncoding` 需与服务端节点的 `etc.cluster.node.codec` 一致（默认 `json`）。
4. 创建客户端：在 Unity 主线程（如 `Awake`、`Start`）中构造 `Client`，并注入 `Tcp` 或 `Ws`。
5. 注册回调：在调用 `Connect` 之前注册 `OnConnect`、`OnReceive`、`OnDisconnect`、`OnHeartbeat`。
6. 建立连接：调用 `Connect`，在 `OnConnect` 中开始业务收发。
7. 启用安全（可选）：服务端开启 `WithSessionSecurity` 后，客户端注入 `SecurityOptions` 并配置服务端公钥。
8. 释放连接：在 `OnDestroy` 或场景切换时调用 `Disconnect`。

最小接入代码：

```csharp
using Due;
using Due.Network;
using Due.Security;
using UnityEngine;

public class NetworkManager : MonoBehaviour
{
    private Client client;

    private void Awake()
    {
        // 与服务端 etc.packet 保持一致
        Packer packer = new Packer(ByteOrder.BigEndian, 2, 2, new Json());

        // 服务端未启用 security 时传 null 即可
        SecurityOptions security = null;
        // security = new SecurityOptions
        // {
        //     Verifier = ServerVerifier.Ecdsa("keys/gate.pub.pem"),
        // };

        client = new Client(new Tcp(packer), packer, security);

        client.OnConnect(c => Debug.Log("连接就绪"));
        client.OnReceive((c, message) => Debug.Log($"route={message.Route}, seq={message.Seq}"));
        client.OnDisconnect(c => Debug.Log("连接断开"));

        client.Connect("127.0.0.1:3553");
    }

    private void OnDestroy()
    {
        client?.Disconnect();
    }
}
```

#### 9.3 配置对照表

协议参数必须与服务端完全一致，否则会出现连接成功但无法解析消息、或直接被网关断开的情况。

| Unity 客户端 | due 服务端配置 | 说明 |
| --- | --- | --- |
| `Packer` 的 `byteOrder` | `[packet] byteOrder` | `big` / `little`，必须一致 |
| `Packer` 的 `routeBytes` | `[packet] routeBytes` | 可选 1 / 2 / 4，必须一致 |
| `Packer` 的 `seqBytes` | `[packet] seqBytes` | 客户端支持 1 / 2 / 4；服务端为 0（不编码序列号）时客户端暂不支持 |
| 单条消息 payload 长度 | `[packet] bufferBytes` | 默认上限 5000 字节，超限会被服务端拒绝 |
| `IEncoding` 实现 | `[cluster.node] codec` | 例如 `json`、`proto`，需与节点保持一致 |
| `SecurityOptions.Verifier` | `[crypto.ecc.signer] publicKey` / `[crypto.rsa.signer] publicKey` | 客户端使用网关公钥验签 |
| `SecurityOptions.Suites` | `gate.WithSessionSecurity(signer, suites...)` | 需与网关启用的套件有交集，客户端默认 AES-256-GCM |
| `SecurityOptions.HandshakeTimeout` | `gate.WithSessionSecurityHandshakeTimeout(...)` | 默认 5000 毫秒 |
| `ServerVerifier.Ecdsa` 的 `hash` | `[crypto.ecc.signer] hash` | 默认 `SHA256` |
| `ServerVerifier.Ecdsa` 的 `delimiter` | `[crypto.ecc.signer] delimiter` | 默认空格 |
| `ServerVerifier.Rsa` 的 `pss` | `[crypto.rsa.signer] padding` | `PSS` 对应 `pss: true`，`PKCS` 对应 `pss: false` |

> 服务端开启会话安全后，节点侧不要再配置静态 `encryptor`，业务 payload 的加密由网关注入的会话密钥负责，避免二次加密。

#### 9.4 帧结构

```
+----------+------------+-----------+---------+-----------+
| size(4B) | header(1B) | route(nB) | seq(mB) | payload   |
+----------+------------+-----------+---------+-----------+
```

* `size`：`header + route + seq + payload` 的总长度（不含自身 4 字节）。
* `header`：bit7 为心跳帧标记，bit0 为握手帧标记。
* 心跳帧：`size(4B) | 0x80 | [服务器时间(8B)]`，是否携带时间由服务端 `heartbeatTime` 决定。
* 握手帧：`size(4B) | 0x01 | 握手数据`，不携带 route 与 seq。
* 安全会话建立后，业务帧的 `payload` 为：`epoch(1B) | counter(8B) | ciphertext | tag(16B)`。

#### 9.5 线程模型与生命周期

* `Client` 在构造时捕获当前 `SynchronizationContext`，网络回调会被派发回该上下文；请在 Unity 主线程创建客户端。
* `OnConnect`、`OnReceive`、`OnDisconnect`、`OnHeartbeat` 回调可直接操作 UI 与场景对象。
* `Send` 可在主线程调用，内部已做发送串行化；但不要在握手完成前发送业务消息（安全模式下会返回 `false`）。
* `Disconnect` 会停止心跳；重复调用是安全的。

#### 9.6 断线重连

* 心跳间隔可在构造 `Client` 时指定，默认 10 秒；服务端开启心跳检测时，客户端心跳间隔应小于服务端检测间隔。
* 安全会话与连接一一对应，每次重新连接都会重新握手，无需手动清理密钥。
* 重连建议重新创建 `Client`（及 `Tcp` / `Ws`）实例，避免复用已断开的传输对象：

```csharp
client.OnDisconnect(c =>
{
    // 延迟后使用新的 Client 实例重连
    // StartCoroutine(ReconnectAfter(1f));
});
```

#### 9.7 联调验证清单

* [ ] 与服务端核对 `byteOrder`、`routeBytes`、`seqBytes`、`bufferBytes`、`codec`
* [ ] `OnConnect` 正常触发（安全模式下为握手完成之后）
* [ ] 小消息（几十字节）与大消息（接近 `bufferBytes`）均可正常收发
* [ ] 心跳运行一段时间后连接保持稳定
* [ ] 安全模式下抓包确认 payload 无业务明文
* [ ] 断开连接后重连正常
* [ ] 运行 `Tests` 下的 EditMode 单元测试

#### 9.8 常见问题

**连接成功但收不到消息**

* 最常见原因是 `routeBytes` / `seqBytes` / `byteOrder` 与服务端 `etc.packet` 不一致。
* 检查客户端编解码器与服务端节点 `codec` 是否一致（如 JSON 对 proto）。
* 检查消息 `route` 是否已在节点注册。

**连接建立后立即断开，或一直收不到回包**

* 客户端启用了 `SecurityOptions` 但网关未启用 `WithSessionSecurity`（或反之）。
* 客户端公钥与网关签名私钥不匹配，或 `hash` / `delimiter` / `padding` 配置不一致。
* 握手超时时间小于实际网络往返时间，可适当调大 `HandshakeTimeout`。
* 服务端启用了会话安全，但节点侧仍配置了静态 `encryptor`。

**单条消息发送失败或服务端报消息过大**

* payload 超过服务端 `etc.packet.bufferBytes`（默认 5000 字节），需要拆分消息或调大服务端配置。

**JSON 解析异常**

* `JsonUtility` 仅支持标记 `[System.Serializable]` 的类型，只序列化 public 字段，不支持字典、匿名对象与顶层数组。
* JSON 的字段名与 C# 字段名必须一致。
* 如需更灵活的 JSON 能力，可实现 `IEncoding` 并注入 Newtonsoft.Json 等实现。

**WebSocket / WebGL**

* WebSocket 需将 API Compatibility Level 设置为 `.NET Standard 2.1` 或 `.NET Framework 4.x`。
* WebGL 不支持 `System.Net.WebSockets`，需基于浏览器原生 WebSocket 实现 `INetwork`；同时需验证 `ECDsa` 等加密 API 在该平台是否可用。

## License

[MIT](https://tldrlegal.com/license/mit-license)
