namespace Due.Tests
{
using System;
using System.Security.Cryptography;
using System.Text;
using Due.Security;
using NUnit.Framework;

// AEAD 会话测试
[TestFixture]
public class SessionTests
{
    // 会话盐值
    private static readonly byte[] Salt = { 0x01, 0x02, 0x03, 0x04 };

    // 构造客户端会话
    private static Session CreateClient(byte[] c2sKey, byte[] s2cKey)
    {
        return new Session(AesGcmSuite.Instance, c2sKey, s2cKey, Salt);
    }

    // 构造服务端会话（收发密钥与客户端相反）
    private static Session CreateServer(byte[] c2sKey, byte[] s2cKey)
    {
        return new Session(AesGcmSuite.Instance, s2cKey, c2sKey, Salt);
    }

    // 生成固定密钥
    private static byte[] Key(byte seed)
    {
        byte[] key = new byte[32];

        for (int i = 0; i < key.Length; i++)
        {
            key[i] = (byte)(seed + i);
        }

        return key;
    }

    // 双向加解密
    [Test]
    public void SealAndOpen_Roundtrip()
    {
        byte[] c2sKey = Key(1);
        byte[] s2cKey = Key(33);
        Session client = CreateClient(c2sKey, s2cKey);
        Session server = CreateServer(c2sKey, s2cKey);
        byte[] request = Encoding.UTF8.GetBytes("hello gate");
        byte[] response = Encoding.UTF8.GetBytes("hello client");

        byte[] sealedRequest = client.SealMessage(request, Direction.ClientToServer, 1001, 7);
        byte[] sealedResponse = server.SealMessage(response, Direction.ServerToClient, 1002, 8);

        CollectionAssert.AreEqual(request, server.OpenMessage(sealedRequest, Direction.ClientToServer, 1001, 7));
        CollectionAssert.AreEqual(response, client.OpenMessage(sealedResponse, Direction.ServerToClient, 1002, 8));
    }

    // 密文信封布局：epoch(1) | counter(8) | ciphertext | tag
    [Test]
    public void Envelope_Layout()
    {
        Session client = CreateClient(Key(1), Key(33));
        byte[] plaintext = Encoding.UTF8.GetBytes("hello");
        byte[] first = client.SealMessage(plaintext, Direction.ClientToServer, 1, 2);
        byte[] second = client.SealMessage(plaintext, Direction.ClientToServer, 1, 2);

        Assert.AreEqual(1, first[0]);
        Assert.AreEqual(25, client.Overhead);
        Assert.AreEqual(plaintext.Length + client.Overhead, first.Length);
        Assert.AreEqual(0UL, ReadCounter(first));
        Assert.AreEqual(1UL, ReadCounter(second));
    }

    // AAD 绑定路由、序列号与方向
    [Test]
    public void Aad_BindsRouteSeqAndDirection()
    {
        byte[] c2sKey = Key(1);
        byte[] s2cKey = Key(33);
        Session client = CreateClient(c2sKey, s2cKey);
        Session server = CreateServer(c2sKey, s2cKey);
        byte[] sealedData = client.SealMessage(Encoding.UTF8.GetBytes("hello"), Direction.ClientToServer, 10, 2);

        Assert.Throws<CryptographicException>(
            () => server.OpenMessage(sealedData, Direction.ClientToServer, 11, 2));
        Assert.Throws<CryptographicException>(
            () => server.OpenMessage(sealedData, Direction.ClientToServer, 10, 3));
        Assert.Throws<CryptographicException>(
            () => server.OpenMessage(sealedData, Direction.ServerToClient, 10, 2));

        // 认证失败不应推进重放窗口
        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes("hello"),
            server.OpenMessage(sealedData, Direction.ClientToServer, 10, 2));
    }

    // 篡改密文必须被拒绝
    [Test]
    public void TamperedPayload_Throws()
    {
        byte[] c2sKey = Key(1);
        byte[] s2cKey = Key(33);
        Session client = CreateClient(c2sKey, s2cKey);
        Session server = CreateServer(c2sKey, s2cKey);
        byte[] sealedData = client.SealMessage(Encoding.UTF8.GetBytes("hello"), Direction.ClientToServer, 1, 1);

        sealedData[sealedData.Length - 1] ^= 0xff;

        Assert.Throws<CryptographicException>(
            () => server.OpenMessage(sealedData, Direction.ClientToServer, 1, 1));
    }

    // 重放消息必须被拒绝
    [Test]
    public void ReplayedMessage_Throws()
    {
        byte[] c2sKey = Key(1);
        byte[] s2cKey = Key(33);
        Session client = CreateClient(c2sKey, s2cKey);
        Session server = CreateServer(c2sKey, s2cKey);
        byte[] sealedData = client.SealMessage(Encoding.UTF8.GetBytes("hello"), Direction.ClientToServer, 1, 1);

        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes("hello"),
            server.OpenMessage(sealedData, Direction.ClientToServer, 1, 1));

        Assert.Throws<CryptographicException>(
            () => server.OpenMessage(sealedData, Direction.ClientToServer, 1, 1));
    }

    // 窗口内的乱序消息应被接受
    [Test]
    public void OutOfOrder_WithinWindow_Accepted()
    {
        byte[] c2sKey = Key(1);
        byte[] s2cKey = Key(33);
        Session client = CreateClient(c2sKey, s2cKey);
        Session server = CreateServer(c2sKey, s2cKey);
        byte[] plaintext = Encoding.UTF8.GetBytes("hello");
        byte[] first = client.SealMessage(plaintext, Direction.ClientToServer, 1, 1);
        byte[] second = client.SealMessage(plaintext, Direction.ClientToServer, 1, 1);
        byte[] third = client.SealMessage(plaintext, Direction.ClientToServer, 1, 1);

        CollectionAssert.AreEqual(plaintext, server.OpenMessage(third, Direction.ClientToServer, 1, 1));
        CollectionAssert.AreEqual(plaintext, server.OpenMessage(first, Direction.ClientToServer, 1, 1));
        CollectionAssert.AreEqual(plaintext, server.OpenMessage(second, Direction.ClientToServer, 1, 1));

        Assert.Throws<CryptographicException>(
            () => server.OpenMessage(first, Direction.ClientToServer, 1, 1));
    }

    // epoch 不匹配必须被拒绝
    [Test]
    public void InvalidEpoch_Throws()
    {
        byte[] c2sKey = Key(1);
        byte[] s2cKey = Key(33);
        Session client = CreateClient(c2sKey, s2cKey);
        Session server = CreateServer(c2sKey, s2cKey);
        byte[] sealedData = client.SealMessage(Encoding.UTF8.GetBytes("hello"), Direction.ClientToServer, 1, 1);

        sealedData[0] = 2;

        Assert.Throws<CryptographicException>(
            () => server.OpenMessage(sealedData, Direction.ClientToServer, 1, 1));
    }

    // 密文过短必须被拒绝
    [Test]
    public void ShortEnvelope_Throws()
    {
        Session client = CreateClient(Key(1), Key(33));

        Assert.Throws<CryptographicException>(
            () => client.OpenMessage(new byte[24], Direction.ServerToClient, 1, 1));
        Assert.Throws<CryptographicException>(
            () => client.OpenMessage(null, Direction.ServerToClient, 1, 1));
    }

    // 构造参数校验
    [Test]
    public void InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new Session(null, Key(1), Key(33), Salt));
        Assert.Throws<ArgumentException>(() => new Session(AesGcmSuite.Instance, Key(1), Key(33), new byte[3]));
    }

    // AAD 编码：version | direction | epoch | route | seq
    [Test]
    public void MakeAad_MatchesCanonicalEncoding()
    {
        byte[] aad = Session.MakeAAD(Direction.ServerToClient, 1, 0x01020304, 0x05060708);

        Assert.AreEqual("0101010102030405060708", TestUtil.ToHex(aad));
    }

    // 读取密文中的计数器
    private static ulong ReadCounter(byte[] sealedData)
    {
        ulong counter = 0;

        for (int i = 1; i <= 8; i++)
        {
            counter = (counter << 8) | sealedData[i];
        }

        return counter;
    }
}
}
