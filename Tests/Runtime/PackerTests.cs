namespace Due.Tests
{
using System;
using Due;
using NUnit.Framework;

// 打包器测试
[TestFixture]
public class PackerTests
{
    // 心跳帧
    [Test]
    public void HeartbeatFrame_IsRecognized()
    {
        Packer packer = new Packer();
        byte[] buffer = packer.PackHeartbeat();
        Frame frame = packer.UnpackFrame(buffer);

        Assert.IsTrue(frame.IsHeartbeat);
        Assert.IsFalse(frame.IsHandshake);
        Assert.IsNull(frame.Millisecond);
        Assert.AreEqual(Packer.HEADER_HEARTBEAT, buffer[4]);

        Packet packet = packer.Unpack(buffer);

        Assert.IsTrue(packet.IsHeartbeat);
        Assert.IsNull(packet.Message);
    }

    // 消息帧
    [Test]
    public void MessageFrame_Roundtrip()
    {
        Packer packer = new Packer();
        byte[] payload = { 0x01, 0x02, 0x03 };
        byte[] buffer = packer.PackMessage(1001, 7, payload);
        Frame frame = packer.UnpackFrame(buffer);

        Assert.IsFalse(frame.IsHeartbeat);
        Assert.IsFalse(frame.IsHandshake);
        Assert.AreEqual(1001, frame.Route);
        Assert.AreEqual(7, frame.Seq);
        CollectionAssert.AreEqual(payload, frame.Payload);

        Packet packet = packer.Unpack(buffer);

        Assert.IsFalse(packet.IsHeartbeat);
        Assert.IsNotNull(packet.Message);
        Assert.AreEqual(1001, packet.Message.Value.Route);
        Assert.AreEqual(7, packet.Message.Value.Seq);
        CollectionAssert.AreEqual(payload, packet.Message.Value.Data.GetBytes());
    }

    // 握手帧不含路由与序列号
    [Test]
    public void HandshakeFrame_Roundtrip()
    {
        Packer packer = new Packer();
        byte[] payload = TestUtil.Hex("0101000401020304");
        byte[] buffer = packer.PackHandshake(payload);
        Frame frame = packer.UnpackFrame(buffer);

        Assert.IsTrue(frame.IsHandshake);
        Assert.IsFalse(frame.IsHeartbeat);
        Assert.AreEqual(0, frame.Route);
        Assert.AreEqual(0, frame.Seq);
        CollectionAssert.AreEqual(payload, frame.Payload);
        Assert.AreEqual(Packer.HEADER_HANDSHAKE, buffer[4]);
        Assert.AreEqual(Packer.DEFAULT_HEADER_BYTES + payload.Length, buffer.Length - Packer.DEFAULT_SIZE_BYTES);
    }

    // 传统解包接口不接受握手帧
    [Test]
    public void Unpack_HandshakeFrame_Throws()
    {
        Packer packer = new Packer();
        byte[] buffer = packer.PackHandshake(new byte[4]);

        Assert.Throws<Exception>(() => packer.Unpack(buffer));
    }

    // 小端字节序
    [Test]
    public void LittleEndian_Roundtrip()
    {
        Packer packer = new Packer(ByteOrder.LittleEndian);
        byte[] payload = { 0xaa, 0xbb };
        byte[] buffer = packer.PackMessage(258, 3, payload);
        Frame frame = packer.UnpackFrame(buffer);

        Assert.AreEqual(258, frame.Route);
        Assert.AreEqual(3, frame.Seq);
        CollectionAssert.AreEqual(payload, frame.Payload);
        Assert.AreEqual(Packer.DEFAULT_HEADER_BYTES + 2 + 2 + payload.Length, packer.UnpackSize(buffer));
    }

    // 非法帧长度
    [Test]
    public void InvalidFrame_Throws()
    {
        Packer packer = new Packer();

        Assert.Throws<Exception>(() => packer.UnpackFrame(new byte[] { 0x00, 0x00, 0x00, 0x05, 0x00 }));
    }
}
}
