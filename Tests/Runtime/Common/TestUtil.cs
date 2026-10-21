namespace Due.Tests
{
using System;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

// 测试辅助工具：十六进制、DER/PEM 构造、签名格式转换
internal static class TestUtil
{
    // id-ecPublicKey
    private static readonly byte[] OID_EC_PUBLIC_KEY = { 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01 };

    // rsaEncryption
    private static readonly byte[] OID_RSA_ENCRYPTION = { 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01 };

    // prime256v1
    private static readonly byte[] OID_P256 = { 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07 };

    // secp384r1
    private static readonly byte[] OID_P384 = { 0x2B, 0x81, 0x04, 0x00, 0x22 };

    // secp521r1
    private static readonly byte[] OID_P521 = { 0x2B, 0x81, 0x04, 0x00, 0x23 };

    // 十六进制字符串转字节数组
    public static byte[] Hex(string value)
    {
        value = value.Replace(" ", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);

        byte[] buffer = new byte[value.Length / 2];

        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = byte.Parse(value.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return buffer;
    }

    // 字节数组转十六进制字符串
    public static string ToHex(byte[] value)
    {
        return BitConverter.ToString(value).Replace("-", string.Empty).ToLowerInvariant();
    }

    // 生成随机字节
    public static byte[] RandomBytes(int length)
    {
        byte[] buffer = new byte[length];

        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(buffer);
        }

        return buffer;
    }

    // 拼接多段数据
    public static byte[] Concat(params byte[][] parts)
    {
        int length = 0;

        foreach (byte[] part in parts)
        {
            if (part != null)
            {
                length += part.Length;
            }
        }

        byte[] buffer = new byte[length];
        int offset = 0;

        foreach (byte[] part in parts)
        {
            if (part != null)
            {
                Buffer.BlockCopy(part, 0, buffer, offset, part.Length);
                offset += part.Length;
            }
        }

        return buffer;
    }

    // 左侧补零到指定长度
    public static byte[] PadLeft(byte[] value, int size)
    {
        if (value.Length == size)
        {
            return value;
        }

        if (value.Length > size)
        {
            throw new ArgumentException("value is larger than the target size", nameof(value));
        }

        byte[] buffer = new byte[size];
        Buffer.BlockCopy(value, 0, buffer, size - value.Length, value.Length);

        return buffer;
    }

    // 导出 ECDSA 公钥 PEM（PKIX）
    public static string EcdsaPublicKeyPem(ECDsa ecdsa)
    {
        ECParameters parameters = ecdsa.ExportParameters(false);
        int fieldSize = (ecdsa.KeySize + 7) / 8;
        byte[] curveOid;

        switch (ecdsa.KeySize)
        {
            case 256:
                curveOid = OID_P256;
                break;
            case 384:
                curveOid = OID_P384;
                break;
            case 521:
                curveOid = OID_P521;
                break;
            default:
                throw new NotSupportedException("unsupported curve");
        }

        byte[] algorithm = DerSequence(DerOid(OID_EC_PUBLIC_KEY), DerOid(curveOid));
        byte[] point = Concat(new byte[] { 0x04 }, PadLeft(parameters.Q.X, fieldSize), PadLeft(parameters.Q.Y, fieldSize));
        byte[] spki = DerSequence(algorithm, DerBitString(point));

        return Pem("PUBLIC KEY", spki);
    }

    // 导出 RSA 公钥 PEM；pkcs1 为 true 时导出 PKCS#1 结构
    public static string RsaPublicKeyPem(RSA rsa, bool pkcs1)
    {
        RSAParameters parameters = rsa.ExportParameters(false);
        byte[] key = DerSequence(DerInteger(parameters.Modulus), DerInteger(parameters.Exponent));

        if (pkcs1)
        {
            return Pem("RSA PUBLIC KEY", key);
        }

        byte[] algorithm = DerSequence(DerOid(OID_RSA_ENCRYPTION), new byte[] { 0x05, 0x00 });
        byte[] spki = DerSequence(algorithm, DerBitString(key));

        return Pem("PUBLIC KEY", spki);
    }

    // 将平台签名归一化为 IEEE P1363（r || s）格式
    public static byte[] NormalizeEcdsaSignature(byte[] signature, int fieldSize)
    {
        if (signature.Length == fieldSize * 2)
        {
            return signature;
        }

        int offset = 0;
        byte[] sequence = ReadDer(signature, ref offset, 0x30);
        int sequenceOffset = 0;
        byte[] r = ReadDer(sequence, ref sequenceOffset, 0x02);
        byte[] s = ReadDer(sequence, ref sequenceOffset, 0x02);
        byte[] result = new byte[fieldSize * 2];

        CopyFixed(r, result, 0, fieldSize);
        CopyFixed(s, result, fieldSize, fieldSize);

        return result;
    }

    // 将 IEEE P1363 签名转换为 due 的十进制文本签名
    public static byte[] DueEcdsaSignature(byte[] signature, int fieldSize, string delimiter = " ")
    {
        BigInteger r = BigEndianToBigInteger(signature, 0, fieldSize);
        BigInteger s = BigEndianToBigInteger(signature, fieldSize, fieldSize);
        string text = r.ToString(CultureInfo.InvariantCulture) + delimiter + s.ToString(CultureInfo.InvariantCulture);

        return Encoding.ASCII.GetBytes(text);
    }

    // 大端字节数组转整数
    public static BigInteger BigEndianToBigInteger(byte[] buffer, int offset, int length)
    {
        BigInteger value = BigInteger.Zero;

        for (int i = 0; i < length; i++)
        {
            value = (value << 8) | buffer[offset + i];
        }

        return value;
    }

    // 生成 PEM 文本
    public static string Pem(string type, byte[] der)
    {
        string base64 = Convert.ToBase64String(der);
        StringBuilder builder = new StringBuilder();

        builder.Append("-----BEGIN ").Append(type).Append("-----\n");

        for (int i = 0; i < base64.Length; i += 64)
        {
            builder.Append(base64.Substring(i, Math.Min(64, base64.Length - i))).Append('\n');
        }

        builder.Append("-----END ").Append(type).Append("-----");

        return builder.ToString();
    }

    // 将值按固定长度拷入目标缓冲区（右对齐）
    private static void CopyFixed(byte[] value, byte[] target, int offset, int size)
    {
        int start = 0;

        while (start < value.Length - 1 && value[start] == 0)
        {
            start++;
        }

        int length = value.Length - start;

        if (length > size)
        {
            throw new CryptographicException("signature component is too large");
        }

        Buffer.BlockCopy(value, start, target, offset + size - length, length);
    }

    // 读取 DER TLV
    private static byte[] ReadDer(byte[] der, ref int offset, byte tag)
    {
        if (der == null || offset >= der.Length || der[offset] != tag)
        {
            throw new CryptographicException("invalid der encoding");
        }

        offset++;

        int length = ReadDerLength(der, ref offset);

        if (offset + length > der.Length)
        {
            throw new CryptographicException("invalid der encoding");
        }

        byte[] value = new byte[length];
        Buffer.BlockCopy(der, offset, value, 0, length);
        offset += length;

        return value;
    }

    // 读取 DER 长度
    private static int ReadDerLength(byte[] der, ref int offset)
    {
        if (offset >= der.Length)
        {
            throw new CryptographicException("invalid der encoding");
        }

        int value = der[offset++];

        if (value < 0x80)
        {
            return value;
        }

        int count = value & 0x7f;

        if (count == 0 || count > 4 || offset + count > der.Length)
        {
            throw new CryptographicException("invalid der encoding");
        }

        int length = 0;

        for (int i = 0; i < count; i++)
        {
            length = (length << 8) | der[offset++];
        }

        return length;
    }

    // 构造 DER SEQUENCE
    private static byte[] DerSequence(params byte[][] parts)
    {
        return DerTlv(0x30, Concat(parts));
    }

    // 构造 DER OID
    private static byte[] DerOid(byte[] oid)
    {
        return DerTlv(0x06, oid);
    }

    // 构造 DER BIT STRING
    private static byte[] DerBitString(byte[] value)
    {
        return DerTlv(0x03, Concat(new byte[] { 0x00 }, value));
    }

    // 构造 DER INTEGER
    private static byte[] DerInteger(byte[] value)
    {
        int offset = 0;

        while (offset < value.Length - 1 && value[offset] == 0)
        {
            offset++;
        }

        byte[] trimmed = new byte[value.Length - offset];
        Buffer.BlockCopy(value, offset, trimmed, 0, trimmed.Length);

        if ((trimmed[0] & 0x80) != 0)
        {
            trimmed = Concat(new byte[] { 0x00 }, trimmed);
        }

        return DerTlv(0x02, trimmed);
    }

    // 构造 DER TLV
    private static byte[] DerTlv(byte tag, byte[] value)
    {
        return Concat(new byte[] { tag }, DerLength(value.Length), value);
    }

    // 构造 DER 长度字段
    private static byte[] DerLength(int length)
    {
        if (length < 0x80)
        {
            return new byte[] { (byte)length };
        }

        if (length <= 0xff)
        {
            return new byte[] { 0x81, (byte)length };
        }

        return new byte[] { 0x82, (byte)(length >> 8), (byte)length };
    }
}
}
