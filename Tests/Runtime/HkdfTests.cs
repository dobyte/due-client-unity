namespace Due.Tests
{
using System.Text;
using Due.Security;
using NUnit.Framework;

// HKDF-SHA256 测试
[TestFixture]
public class HkdfTests
{
    // RFC 5869 测试用例 1
    [Test]
    public void Rfc5869Case1_Matches()
    {
        byte[] ikm = new byte[22];

        for (int i = 0; i < ikm.Length; i++)
        {
            ikm[i] = 0x0b;
        }

        byte[] salt = TestUtil.Hex("000102030405060708090a0b0c");
        byte[] info = TestUtil.Hex("f0f1f2f3f4f5f6f7f8f9");
        byte[] output = Hkdf.Sha256(ikm, salt, info, 42);

        Assert.AreEqual(
            "3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865",
            TestUtil.ToHex(output));
    }

    // RFC 5869 测试用例 3：空 salt 与空 info
    [Test]
    public void Rfc5869Case3_Matches()
    {
        byte[] ikm = new byte[22];

        for (int i = 0; i < ikm.Length; i++)
        {
            ikm[i] = 0x0b;
        }

        byte[] output = Hkdf.Sha256(ikm, new byte[0], new byte[0], 42);

        Assert.AreEqual(
            "8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d9d201395faa4b61a96c8",
            TestUtil.ToHex(output));
    }

    // 派生长度应精确匹配请求值
    [Test]
    public void OutputLength_MatchesRequest()
    {
        byte[] output = Hkdf.Sha256(Encoding.UTF8.GetBytes("ikm"), Encoding.UTF8.GetBytes("salt"), Encoding.UTF8.GetBytes("info"), 132);

        Assert.AreEqual(132, output.Length);
    }

    // 相同输入必须得到相同输出
    [Test]
    public void Derivation_IsDeterministic()
    {
        byte[] ikm = Encoding.UTF8.GetBytes("due-ikm");
        byte[] salt = Encoding.UTF8.GetBytes("due-salt");
        byte[] info = Encoding.UTF8.GetBytes("due-info");

        Assert.AreEqual(
            TestUtil.ToHex(Hkdf.Sha256(ikm, salt, info, 64)),
            TestUtil.ToHex(Hkdf.Sha256(ikm, salt, info, 64)));
    }
}
}
