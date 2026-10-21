namespace Due.Tests
{
using System;
using System.Security.Cryptography;
using System.Text;
using Due.Security;
using NUnit.Framework;

// 服务端公钥验证器测试
[TestFixture]
public class VerifierTests
{
    // ECDSA 验签正常流程
    [Test]
    public void Ecdsa_ValidSignature_ReturnsTrue()
    {
        using (ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            byte[] data = Encoding.UTF8.GetBytes("due-handshake-v1-signature-data");
            byte[] digest = Sha256(data);
            int fieldSize = (ecdsa.KeySize + 7) / 8;
            byte[] signature = TestUtil.DueEcdsaSignature(
                TestUtil.NormalizeEcdsaSignature(ecdsa.SignHash(digest), fieldSize),
                fieldSize);
            HandshakeVerifier verifier = ServerVerifier.Ecdsa(TestUtil.EcdsaPublicKeyPem(ecdsa));

            Assert.IsTrue(verifier(data, signature));
            Assert.IsFalse(verifier(Encoding.UTF8.GetBytes("tampered"), signature));
        }
    }

    // ECDSA 自定义分隔符
    [Test]
    public void Ecdsa_CustomDelimiter_Works()
    {
        using (ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            byte[] data = Encoding.UTF8.GetBytes("due-handshake-v1-signature-data");
            byte[] digest = Sha256(data);
            int fieldSize = (ecdsa.KeySize + 7) / 8;
            byte[] signature = TestUtil.DueEcdsaSignature(
                TestUtil.NormalizeEcdsaSignature(ecdsa.SignHash(digest), fieldSize),
                fieldSize,
                ",");
            HandshakeVerifier verifier = ServerVerifier.Ecdsa(TestUtil.EcdsaPublicKeyPem(ecdsa), null, ",");

            Assert.IsTrue(verifier(data, signature));
        }
    }

    // ECDSA 公钥不匹配
    [Test]
    public void Ecdsa_WrongPublicKey_ReturnsFalse()
    {
        using (ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        using (ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            byte[] data = Encoding.UTF8.GetBytes("due-handshake-v1-signature-data");
            byte[] digest = Sha256(data);
            int fieldSize = (signer.KeySize + 7) / 8;
            byte[] signature = TestUtil.DueEcdsaSignature(
                TestUtil.NormalizeEcdsaSignature(signer.SignHash(digest), fieldSize),
                fieldSize);
            HandshakeVerifier verifier = ServerVerifier.Ecdsa(TestUtil.EcdsaPublicKeyPem(other));

            Assert.IsFalse(verifier(data, signature));
        }
    }

    // ECDSA 非法签名格式
    [Test]
    public void Ecdsa_MalformedSignature_ReturnsFalse()
    {
        using (ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            HandshakeVerifier verifier = ServerVerifier.Ecdsa(TestUtil.EcdsaPublicKeyPem(ecdsa));

            Assert.IsFalse(verifier(Encoding.UTF8.GetBytes("data"), Encoding.ASCII.GetBytes("not-a-signature")));
            Assert.IsFalse(verifier(Encoding.UTF8.GetBytes("data"), null));
        }
    }

    // RSA PKCS#1 公钥 + PKCS#1 v1.5 填充
    [Test]
    public void Rsa_Pkcs1_ValidSignature_ReturnsTrue()
    {
        using (RSA rsa = CreateRsa())
        {
            byte[] data = Encoding.UTF8.GetBytes("due-handshake-v1-rsa-data");
            byte[] signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            HandshakeVerifier verifier = ServerVerifier.Rsa(TestUtil.RsaPublicKeyPem(rsa, true), null, false);

            Assert.IsTrue(verifier(data, signature));
            Assert.IsFalse(verifier(Encoding.UTF8.GetBytes("tampered"), signature));
        }
    }

    // RSA PKIX 公钥 + PSS 填充
    [Test]
    public void Rsa_Pss_ValidSignature_ReturnsTrue()
    {
        using (RSA rsa = CreateRsa())
        {
            byte[] data = Encoding.UTF8.GetBytes("due-handshake-v1-rsa-data");
            byte[] signature;

            try
            {
                signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            }
            catch (NotImplementedException)
            {
                Assert.Ignore("当前平台不支持 RSA PSS");
                return;
            }
            catch (PlatformNotSupportedException)
            {
                Assert.Ignore("当前平台不支持 RSA PSS");
                return;
            }

            HandshakeVerifier verifier = ServerVerifier.Rsa(TestUtil.RsaPublicKeyPem(rsa, false));

            Assert.IsTrue(verifier(data, signature));
            Assert.IsFalse(verifier(Encoding.UTF8.GetBytes("tampered"), signature));
        }
    }

    // 非法 PEM 内容
    [Test]
    public void InvalidPem_Throws()
    {
        Assert.Throws<FormatException>(() => ServerVerifier.Ecdsa("not-a-pem"));
        Assert.Throws<FormatException>(() => ServerVerifier.Rsa("not-a-pem"));
    }

    // 创建 RSA 测试密钥
    private static RSA CreateRsa()
    {
        RSA rsa = RSA.Create();
        rsa.KeySize = 2048;

        return rsa;
    }

    // 计算 SHA-256
    private static byte[] Sha256(byte[] data)
    {
        using (SHA256 sha = SHA256.Create())
        {
            return sha.ComputeHash(data);
        }
    }
}
}
