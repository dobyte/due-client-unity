namespace Due.Network
{
using System;
using System.Net;
using System.Net.Sockets;

// TCP 传输层实现。
// 注意：连接地址需使用 "ip:port" 形式（如 "127.0.0.1:3553"）。
public class Tcp : INetwork
{
    // Socket
    private Socket socket;
    // 发送锁，避免并发发送导致帧交错
    private readonly object sendLock = new object();
    // 打包器（用于按字节序解析长度前缀）
    private readonly Packer packer;
    // 是否已触发断开通知（防止重复回调）
    private bool disconnected;

    public Action OnConnected { get; set; }
    public Action OnDisconnected { get; set; }
    public Action<byte[]> OnPacket { get; set; }

    public Tcp(Packer packer)
    {
        this.packer = packer;
    }

    // 接收状态（缓冲区 + 已读偏移）
    private class ReceiveState
    {
        public byte[] Buffer = new byte[0];
        public int Offset;
    }

    // 连接服务器
    public bool Connect(string addr)
    {
        try
        {
            IPEndPoint iep = IPEndPoint.Parse(addr);
            this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            this.socket.BeginConnect(iep, this.HandleConnected, this.socket);

            return true;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError(e);
            return false;
        }
    }

    // 处理连接成功请求
    private void HandleConnected(IAsyncResult ar)
    {
        if (this.socket == null)
        {
            return;
        }

        try
        {
            this.socket.EndConnect(ar);
            this.ReceiveAsync();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError(e);
            this.NotifyDisconnected();
            return;
        }

        this.OnConnected?.Invoke();
    }

    // 当前是否已连接
    public bool IsConnected()
    {
        return this.socket != null && this.socket.Connected;
    }

    // 发送完整数据包
    public bool Send(byte[] buffer)
    {
        if (this.socket != null && this.IsConnected())
        {
            try
            {
                lock (this.sendLock)
                {
                    this.socket.Send(buffer);
                }
                return true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(e);
            }
        }

        return false;
    }

    // 断开连接
    public void Disconnect()
    {
        if (this.socket == null)
        {
            return;
        }

        try
        {
            this.socket.Shutdown(SocketShutdown.Both);
            this.socket.Close();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError(e);
        }
        finally
        {
            this.socket = null;
        }

        this.NotifyDisconnected();
    }

    // 触发断开通知（防止重复）
    private void NotifyDisconnected()
    {
        if (this.disconnected)
        {
            return;
        }
        this.disconnected = true;
        this.OnDisconnected?.Invoke();
    }

    // 异步接收消息
    private void ReceiveAsync()
    {
        this.ReceiveSize();
    }

    // 接收包大小消息
    private void ReceiveSize()
    {
        if (this.socket != null)
        {
            try
            {
                ReceiveState state = new ReceiveState { Buffer = new byte[Packer.DEFAULT_SIZE_BYTES] };
                this.socket.BeginReceive(state.Buffer, 0, state.Buffer.Length, SocketFlags.None, this.HandleReceivedSize, state);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(e);
                this.NotifyDisconnected();
            }
        }
    }

    // 处理接收到的包大小
    private void HandleReceivedSize(IAsyncResult ar)
    {
        if (this.socket == null)
        {
            return;
        }

        try
        {
            if (!(ar.AsyncState is ReceiveState state))
            {
                return;
            }

            int length = this.socket.EndReceive(ar);
            if (length <= 0)
            {
                // 对端关闭连接
                this.Disconnect();
                return;
            }

            state.Offset += length;
            if (state.Offset < state.Buffer.Length)
            {
                // 长度头未收全（TCP 分片），继续读取剩余字节
                this.socket.BeginReceive(state.Buffer, state.Offset, state.Buffer.Length - state.Offset, SocketFlags.None, this.HandleReceivedSize, state);
                return;
            }

            int size = Packer.DEFAULT_SIZE_BYTES + this.packer.UnpackSize(state.Buffer);

            byte[] buff = new byte[size];

            Array.Copy(state.Buffer, buff, Packer.DEFAULT_SIZE_BYTES);

            ReceiveState bodyState = new ReceiveState { Buffer = buff, Offset = Packer.DEFAULT_SIZE_BYTES };
            this.socket.BeginReceive(buff, Packer.DEFAULT_SIZE_BYTES, size - Packer.DEFAULT_SIZE_BYTES, SocketFlags.None, this.HandleReceivedBody, bodyState);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError(e);
            this.NotifyDisconnected();
        }
    }

    // 处理接收到的包体数据
    private void HandleReceivedBody(IAsyncResult ar)
    {
        if (this.socket == null)
        {
            return;
        }

        try
        {
            if (!(ar.AsyncState is ReceiveState state))
            {
                return;
            }

            int length = this.socket.EndReceive(ar);
            if (length <= 0)
            {
                // 对端关闭连接
                this.Disconnect();
                return;
            }

            state.Offset += length;
            if (state.Offset < state.Buffer.Length)
            {
                // 包体未收全（TCP 分片），继续读取剩余字节
                this.socket.BeginReceive(state.Buffer, state.Offset, state.Buffer.Length - state.Offset, SocketFlags.None, this.HandleReceivedBody, state);
                return;
            }

            this.OnPacket?.Invoke(state.Buffer);

            this.ReceiveAsync();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError(e);
            this.NotifyDisconnected();
        }
    }
}
}
