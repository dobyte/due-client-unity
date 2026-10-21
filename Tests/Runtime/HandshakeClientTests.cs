namespace Due.Tests
{
using System;
using System.Security.Cryptography;
using System.Text;
using Due.Security;
using NUnit.Framework;

// 客户端握手测试
[TestFixture]
public class HandshakeClientTests
{
    // 完整握手并进行双向消息交换
    [Test]
    public void Handshake_EstablishesSession()
    {
        using (ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            HandshakeClient client = new HandshakeClient(ServerVerifier.Ecdsa(TestUtil.EcdsaPublicKeyPem(signer)));
            TestHandshakeServer server = new TestHandshakeServer(signer);
            byte[] hello = client.Hello();
            byte[] serverHello = server.Accept(hello);
            Session clientSession = client.Handle(serverHello, out byte[] finished);
            Session serverSession = server.Confirm(finished);

            Assert.IsNotNull(clientSession);
            Assert.IsNotNull(serverSession);
            Assert.AreEqual(1, clientSession.Epoch);
            Assert.AreEqual(25, clientSession.Overhead);

            // 客户端 → 服务端
            byte[] request = Encoding.UTF8.GetBytes("hello gate");
            byte[] sealedRequest = clientSession.SealMessage(request, Direction.ClientToServer, 1001, 7);
            CollectionAssert.AreEqual(request, serverSession.OpenMessage(sealedRequest, Direction.ClientToServer, 1001, 7));

            // 服务端 → 客户端（多分组载荷）
            byte[] response = new byte[300];

            for (int i = 0; i < response.Length; i++)
            {
                response[i] = (byte)i;
            }

            byte[] sealedResponse = serverSession.SealMessage(response, Direction.ServerToClient, 1002, 8);
            CollectionAssert.AreEqual(response, clientSession.OpenMessage(sealedResponse, Direction.ServerToClient, 1002, 8));
        }
    }

    // 服务端签名非法时必须拒绝
    [Test]
    public void InvalidServerSignature_Throws()
    {
        using (ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            HandshakeClient client = new HandshakeClient(ServerVerifier.Ecdsa(TestUtil.EcdsaPublicKeyPem(signer)));
            TestHandshakeServer server = new TestHandshakeServer(signer);
            byte[] serverHello = server.Accept(client.Hello());

            // 帧头(4) + suite(1) + random(32) + publicKey(32) + 签名长度(2) 之后为签名
            serverHello[4 + 1 + 32 + 32 + 2] ^= 0xff;

            Assert.Throws<CryptographicException>(() => client.Handle(serverHello, out byte[] finished));
        }
    }

    // 服务端收尾 MAC 非法时必须拒绝
    [Test]
    public void InvalidServerFinishedMac_Throws()
    {
        using (ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            HandshakeClient client = new HandshakeClient(ServerVerifier.Ecdsa(TestUtil.EcdsaPublicKeyPem(signer)));
            TestHandshakeServer server = new TestHandshakeServer(signer);
            byte[] serverHello = server.Accept(client.Hello());

            serverHello[serverHello.Length - 1] ^= 0xff;

            Assert.Throws<CryptographicException>(() => client.Handle(serverHello, out byte[] finished));
        }
    }

    // 公钥不匹配时必须拒绝
    [Test]
    public void WrongServerPublicKey_Throws()
    {
        using (ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        using (ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            HandshakeClient client = new HandshakeClient(ServerVerifier.Ecdsa(TestUtil.EcdsaPublicKeyPem(other)));
            TestHandshakeServer server = new TestHandshakeServer(signer);
            byte[] serverHello = server.Accept(client.Hello());

            Assert.Throws<CryptographicException>(() => client.Handle(serverHello, out byte[] finished));
        }
    }

    // 服务端返回非 ServerHello 帧时必须拒绝
    [Test]
    public void UnexpectedFrameType_Throws()
    {
        using (ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            HandshakeClient client = new HandshakeClient(ServerVerifier.Ecdsa(TestUtil.EcdsaPublicKeyPem(signer)));
            byte[] frame = client.Hello();

            frame[1] = 3;

            Assert.Throws<CryptographicException>(() => client.Handle(frame, out byte[] finished));
        }
    }

    // 握手状态机不允许乱序调用
    [Test]
    public void StateMachine_RejectsOutOfOrderCalls()
    {
        using (ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            HandshakeClient client = new HandshakeClient(ServerVerifier.Ecdsa(TestUtil.EcdsaPublicKeyPem(signer)));

            Assert.Throws<InvalidOperationException>(() => client.Handle(new byte[4], out byte[] finished));

            byte[] hello = client.Hello();

            Assert.Throws<InvalidOperationException>(() => client.Hello());
            Assert.AreEqual(1, hello[0]);
        }
    }

    // 必须提供服务端签名验证器
    [Test]
    public void NullVerifier_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new HandshakeClient(null));
    }
}
}
