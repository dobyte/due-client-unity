namespace Due.Tests
{
using System;
using System.Security.Cryptography;
using System.Text;
using Due.Security;

// 按照 due 服务端握手流程实现的测试服务端，用于验证客户端握手逻辑
internal sealed class TestHandshakeServer
{
    // 握手协议版本
    private const byte VERSION = 1;
    // ClientHello 帧类型
    private const byte FRAME_CLIENT_HELLO = 1;
    // ServerHello 帧类型
    private const byte FRAME_SERVER_HELLO = 2;
    // Finished 帧类型
    private const byte FRAME_FINISHED = 3;
    // 随机数长度
    private const int RANDOM_SIZE = 32;
    // 公钥长度
    private const int KEY_SIZE = 32;
    // MAC 长度
    private const int MAC_SIZE = 32;
    // 密钥材料长度
    private const int KEY_MATERIAL_SIZE = 32 * 4 + 4;
    // 握手转录标签
    private const string LABEL = "due-handshake-v1";

    // 服务端签名器
    private readonly ECDsa signer;
    // 协商套件
    private readonly IAeadSuite suite;

    // 握手转录
    private byte[] transcript;
    // 客户端收尾密钥
    private byte[] c2sFinish;
    // 服务端会话
    private Session session;
    // 握手状态
    private int state;

    public TestHandshakeServer(ECDsa signer, IAeadSuite suite = null)
    {
        this.signer = signer;
        this.suite = suite ?? AesGcmSuite.Instance;
    }

    // 服务端会话
    public Session Session => this.session;

    // 处理 ClientHello，返回 ServerHello 帧
    public byte[] Accept(byte[] frame)
    {
        if (this.state != 0)
        {
            throw new InvalidOperationException("handshake: unexpected state");
        }

        DecodeFrame(frame, out byte type, out byte[] hello);

        if (type != FRAME_CLIENT_HELLO)
        {
            throw new CryptographicException("handshake: unexpected frame type");
        }

        if (hello.Length < RANDOM_SIZE + KEY_SIZE + 1)
        {
            throw new CryptographicException("handshake: invalid client hello");
        }

        int count = hello[RANDOM_SIZE + KEY_SIZE];

        if (hello.Length != RANDOM_SIZE + KEY_SIZE + 1 + count)
        {
            throw new CryptographicException("handshake: invalid client hello");
        }

        bool offered = false;

        for (int i = 0; i < count; i++)
        {
            if (hello[RANDOM_SIZE + KEY_SIZE + 1 + i] == this.suite.Id)
            {
                offered = true;
            }
        }

        if (!offered)
        {
            throw new CryptographicException("handshake: no common aead suite");
        }

        byte[] clientRandom = Slice(hello, 0, RANDOM_SIZE);
        byte[] clientPublicKey = Slice(hello, RANDOM_SIZE, KEY_SIZE);
        byte[] privateKey = X25519.GeneratePrivateKey();
        byte[] publicKey = X25519.GetPublicKey(privateKey);
        byte[] random = CryptoUtil.RandomBytes(RANDOM_SIZE);
        byte[] prefix = CryptoUtil.Concat(new byte[] { this.suite.Id }, random, publicKey);
        byte[] label = Encoding.ASCII.GetBytes(LABEL);
        byte[] signed = CryptoUtil.Sha256(label, hello, prefix);
        byte[] digest = CryptoUtil.Sha256(signed);
        int fieldSize = (this.signer.KeySize + 7) / 8;
        byte[] signature = TestUtil.DueEcdsaSignature(
            TestUtil.NormalizeEcdsaSignature(this.signer.SignHash(digest), fieldSize),
            fieldSize);
        byte[] transcript = CryptoUtil.Sha256(hello, prefix, signature);
        byte[] shared = X25519.ComputeSharedKey(privateKey, clientPublicKey);
        byte[] salt = CryptoUtil.Concat(clientRandom, random);
        byte[] material = Hkdf.Sha256(shared, salt, CryptoUtil.Concat(label, transcript), KEY_MATERIAL_SIZE);
        byte[] c2sKey = Slice(material, 0, 32);
        byte[] s2cKey = Slice(material, 32, 32);
        byte[] c2sFinish = Slice(material, 64, 32);
        byte[] s2cFinish = Slice(material, 96, 32);
        byte[] sessionSalt = Slice(material, 128, 4);
        byte[] mac = CryptoUtil.HmacSha256(s2cFinish, transcript);
        byte[] body = CryptoUtil.Concat(
            new byte[] { this.suite.Id },
            random,
            publicKey,
            new byte[] { (byte)(signature.Length >> 8), (byte)signature.Length },
            signature,
            mac);

        this.transcript = transcript;
        this.c2sFinish = c2sFinish;
        this.session = new Session(this.suite, s2cKey, c2sKey, sessionSalt);
        this.state = 1;

        return EncodeFrame(FRAME_SERVER_HELLO, body);
    }

    // 处理 Finished 帧，返回服务端会话
    public Session Confirm(byte[] frame)
    {
        if (this.state != 1)
        {
            throw new InvalidOperationException("handshake: unexpected state");
        }

        DecodeFrame(frame, out byte type, out byte[] body);

        if (type != FRAME_FINISHED || body.Length != MAC_SIZE)
        {
            throw new CryptographicException("handshake: invalid finished frame");
        }

        byte[] expected = CryptoUtil.HmacSha256(this.c2sFinish, this.transcript);

        if (!CryptoUtil.FixedTimeEquals(expected, body))
        {
            throw new CryptographicException("handshake: invalid finished mac");
        }

        this.state = 2;

        return this.session;
    }

    // 编码握手帧
    private static byte[] EncodeFrame(byte type, byte[] body)
    {
        byte[] frame = new byte[4 + body.Length];

        frame[0] = VERSION;
        frame[1] = type;
        frame[2] = (byte)(body.Length >> 8);
        frame[3] = (byte)body.Length;
        Buffer.BlockCopy(body, 0, frame, 4, body.Length);

        return frame;
    }

    // 解码握手帧
    private static void DecodeFrame(byte[] frame, out byte type, out byte[] body)
    {
        if (frame == null || frame.Length < 4 || frame[0] != VERSION)
        {
            throw new CryptographicException("handshake: invalid frame");
        }

        int length = (frame[2] << 8) | frame[3];

        if (length != frame.Length - 4)
        {
            throw new CryptographicException("handshake: invalid frame");
        }

        type = frame[1];
        body = Slice(frame, 4, length);
    }

    // 从缓冲区截取一段
    private static byte[] Slice(byte[] source, int offset, int count)
    {
        byte[] buffer = new byte[count];
        Buffer.BlockCopy(source, offset, buffer, 0, count);

        return buffer;
    }
}
}
