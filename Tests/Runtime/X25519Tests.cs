namespace Due.Tests
{
using System;
using Due.Security;
using NUnit.Framework;

// X25519 密钥交换测试
[TestFixture]
public class X25519Tests
{
    // RFC 7748 测试向量
    [Test]
    public void Rfc7748Vectors_Match()
    {
        byte[] alicePrivate = TestUtil.Hex("77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a");
        byte[] alicePublic = TestUtil.Hex("8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a");
        byte[] bobPrivate = TestUtil.Hex("5dab087e624a8a4b79e17f8b83800ee66f3bb1292618b6fd1c2f8b27ff88e0eb");
        byte[] bobPublic = TestUtil.Hex("de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f");
        byte[] shared = TestUtil.Hex("4a5d9d5ba4ce2de1728e3bf480350f25e07e21c947d19e3376f09b3c1e161742");

        Assert.AreEqual(TestUtil.ToHex(alicePublic), TestUtil.ToHex(X25519.GetPublicKey(alicePrivate)));
        Assert.AreEqual(TestUtil.ToHex(bobPublic), TestUtil.ToHex(X25519.GetPublicKey(bobPrivate)));
        Assert.AreEqual(TestUtil.ToHex(shared), TestUtil.ToHex(X25519.ComputeSharedKey(alicePrivate, bobPublic)));
        Assert.AreEqual(TestUtil.ToHex(shared), TestUtil.ToHex(X25519.ComputeSharedKey(bobPrivate, alicePublic)));
    }

    // 双方协商出的共享密钥必须一致
    [Test]
    public void SharedSecret_IsSymmetric()
    {
        byte[] alicePrivate = X25519.GeneratePrivateKey();
        byte[] bobPrivate = X25519.GeneratePrivateKey();
        byte[] alicePublic = X25519.GetPublicKey(alicePrivate);
        byte[] bobPublic = X25519.GetPublicKey(bobPrivate);

        byte[] aliceShared = X25519.ComputeSharedKey(alicePrivate, bobPublic);
        byte[] bobShared = X25519.ComputeSharedKey(bobPrivate, alicePublic);

        Assert.AreEqual(32, alicePublic.Length);
        Assert.AreEqual(32, aliceShared.Length);
        Assert.AreEqual(TestUtil.ToHex(aliceShared), TestUtil.ToHex(bobShared));
    }

    // 非法密钥长度
    [Test]
    public void InvalidKeySize_Throws()
    {
        Assert.Throws<ArgumentException>(() => X25519.GetPublicKey(new byte[16]));
        Assert.Throws<ArgumentException>(() => X25519.ComputeSharedKey(new byte[32], new byte[16]));
        Assert.Throws<ArgumentException>(() => X25519.ComputeSharedKey(new byte[16], new byte[32]));
    }
}
}
