namespace Due.Tests
{
using System;
using System.Security.Cryptography;
using System.Text;
using Due.Security;
using NUnit.Framework;

// AES-256-GCM 套件测试
[TestFixture]
public class AesGcmTests
{
    // NIST 空明文向量（AES-256）
    [Test]
    public void NistVector_EmptyPlaintext_Matches()
    {
        IAeadCipher cipher = AesGcmSuite.Instance.Create(new byte[32]);
        byte[] output = cipher.Seal(new byte[12], Array.Empty<byte>(), Array.Empty<byte>());

        Assert.AreEqual("530f8afbc74536b9a963b4f1c4cb738b", TestUtil.ToHex(output));
    }

    // NIST 单分组向量（AES-256）
    [Test]
    public void NistVector_SingleBlock_Matches()
    {
        IAeadCipher cipher = AesGcmSuite.Instance.Create(new byte[32]);
        byte[] output = cipher.Seal(new byte[12], new byte[16], Array.Empty<byte>());

        Assert.AreEqual(
            "cea7403d4d606b6e074ec5d3baf39d18d0d1c8a799996bf0265b98b5d48ab919",
            TestUtil.ToHex(output));
    }

    // 多分组、带尾块与 AAD 的向量（由 Go crypto/cipher 生成）
    [Test]
    public void MultiBlockWithAad_Matches()
    {
        byte[] key = new byte[32];
        byte[] nonce = new byte[12];
        byte[] plaintext = new byte[128];

        for (int i = 0; i < key.Length; i++)
        {
            key[i] = (byte)i;
        }

        for (int i = 0; i < nonce.Length; i++)
        {
            nonce[i] = (byte)i;
        }

        for (int i = 0; i < plaintext.Length; i++)
        {
            plaintext[i] = (byte)i;
        }

        byte[] aad = Encoding.UTF8.GetBytes("due-aad");
        IAeadCipher cipher = AesGcmSuite.Instance.Create(key);
        byte[] output = cipher.Seal(nonce, plaintext, aad);

        Assert.AreEqual(128 + 16, output.Length);
        Assert.AreEqual(
            "4703d418c1e0c41c85489d80bde4766293c79527e46e496b207eff9e01741ead" +
            "21318cdf8be434bf5c8d55c6a4aa0617de6852be6ee395ed07ae102224decbd1" +
            "b07d843997946026541de025a3c240a768db9b312236053fa5a4c49724ade7d2" +
            "ab993b85c024ae7df67554d924184f10ca1610d897b053ed35862db6fe9b1071" +
            "c48444509728200396812633c3920696",
            TestUtil.ToHex(output));

        CollectionAssert.AreEqual(plaintext, cipher.Open(nonce, output, aad));
    }

    // 篡改密文必须被拒绝
    [Test]
    public void TamperedCiphertext_Throws()
    {
        IAeadCipher cipher = AesGcmSuite.Instance.Create(new byte[32]);
        byte[] sealedData = cipher.Seal(new byte[12], Encoding.UTF8.GetBytes("due security"), Array.Empty<byte>());

        sealedData[0] ^= 0xff;

        Assert.Throws<CryptographicException>(() => cipher.Open(new byte[12], sealedData, Array.Empty<byte>()));
    }

    // 篡改 AAD 必须被拒绝
    [Test]
    public void TamperedAad_Throws()
    {
        IAeadCipher cipher = AesGcmSuite.Instance.Create(new byte[32]);
        byte[] aad = TestUtil.Hex("0100010000000100000002");
        byte[] sealedData = cipher.Seal(new byte[12], Encoding.UTF8.GetBytes("due security"), aad);

        Assert.Throws<CryptographicException>(
            () => cipher.Open(new byte[12], sealedData, TestUtil.Hex("0100010000000100000003")));
    }

    // 密文过短必须被拒绝
    [Test]
    public void ShortSealedData_Throws()
    {
        IAeadCipher cipher = AesGcmSuite.Instance.Create(new byte[32]);

        Assert.Throws<CryptographicException>(() => cipher.Open(new byte[12], new byte[15], Array.Empty<byte>()));
    }

    // 非法密钥长度
    [Test]
    public void InvalidKeySize_Throws()
    {
        Assert.Throws<ArgumentException>(() => AesGcmSuite.Instance.Create(new byte[16]));
        Assert.Throws<ArgumentException>(() => AesGcmSuite.Instance.Create(null));
    }

    // 非法 nonce 长度
    [Test]
    public void InvalidNonceSize_Throws()
    {
        IAeadCipher cipher = AesGcmSuite.Instance.Create(new byte[32]);

        Assert.Throws<ArgumentException>(() => cipher.Seal(new byte[8], new byte[1], Array.Empty<byte>()));
        Assert.Throws<ArgumentException>(() => cipher.Open(new byte[8], new byte[16], Array.Empty<byte>()));
    }

    // 空明文也要产生认证标签
    [Test]
    public void EmptyPlaintext_ProducesTagOnly()
    {
        IAeadCipher cipher = AesGcmSuite.Instance.Create(new byte[32]);
        byte[] output = cipher.Seal(new byte[12], null, null);

        Assert.AreEqual(16, output.Length);
        Assert.AreEqual(0, cipher.Open(new byte[12], output, null).Length);
    }
}
}
