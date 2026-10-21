using System;
using System.Collections;
using Due;
using Due.Network;
using Due.Security;
using UnityEngine;

// 使用示例（可删除）：演示如何创建客户端、注册回调并收发消息。
public class DueClientExample : MonoBehaviour
{
    private Client client;

    private void Start()
    {
        // 创建打包器（可自定义字节序、路由字节数、序列号字节数、编解码器）
        Packer packer = new Packer();

        // TCP 传输
        // Client client = new Client(new Tcp(packer), packer);

        // WebSocket 传输
        client = new Client(new Ws(packer), packer);

        // 会话安全（可选）：服务端启用 security 时，需提供服务端公钥用于验签
        // SecurityOptions security = new SecurityOptions
        // {
        //     Verifier = ServerVerifier.Ecdsa("keys/gate.pub.pem"),
        // };
        // client = new Client(new Ws(packer), packer, security);

        client.OnConnect(c =>
        {
            Debug.Log("连接成功");

            // 周期性发送消息
            StartCoroutine(SendLoop(c));
        });

        client.OnDisconnect(c =>
        {
            Debug.Log("连接断开");
        });

        client.OnReceive((c, message) =>
        {
            ChatMessage data = message.Data?.GetObject<ChatMessage>();
            Debug.Log($"收到消息; route: {message.Route}, seq: {message.Seq}, content: {data?.Content}");
        });

        client.OnHeartbeat((c, millisecond) =>
        {
            if (millisecond != null)
            {
                Debug.Log($"服务器心跳时间戳: {millisecond}ms");
            }
        });

        // TCP 地址形如 "127.0.0.1:3553"；WebSocket 地址形如 "ws://127.0.0.1:3553"
        client.Connect("ws://127.0.0.1:3553");
    }

    private IEnumerator SendLoop(Client c)
    {
        while (true)
        {
            yield return new WaitForSeconds(1f);

            // 发送消息（route=1, seq=1）
            c.Send(1, 1, new ChatMessage { Content = "hello server" });
        }
    }

    private void OnDestroy()
    {
        client?.Disconnect();
    }
}

// 消息体：JsonUtility 仅序列化 public 字段，需标记 [Serializable]
[Serializable]
public class ChatMessage
{
    public string Content;
}
