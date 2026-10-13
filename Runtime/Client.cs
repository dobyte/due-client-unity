namespace Due
{
using System;
using System.Threading;
using System.Timers;
using Due.Network;

// 连接处理器
public delegate void ConnectHandler(Client client);

// 断开连接处理器
public delegate void DisconnectHandler(Client client);

// 消息接收处理器
public delegate void ReceiveHandler(Client client, Message message);

// 心跳消息处理器
public delegate void HeartbeatHandler(Client client, long? millisecond);

public class Client
{
    // 默认心跳间隔
    const int DEFAULT_HEARTBEAT_INTERVAL = 10 * 1000;
    // 拨号地址
    private string addr;
    // 网络层
    private readonly INetwork network;
    // 打包器
    private readonly Packer packer;
    // 定时器
    private readonly Timer timer;
    // 心跳包
    private readonly byte[] heartbeatBuffer;
    // 主线程同步上下文：用于将后台线程回调派发回 Unity 主线程
    private readonly SynchronizationContext syncContext;

    // 连接处理器
    private ConnectHandler connectHandler;
    // 断开连接处理器
    private DisconnectHandler disconnectHandler;
    // 消息接收处理器
    private ReceiveHandler receiveHandler;
    // 心跳消息处理器
    private HeartbeatHandler heartbeatHandler;

    public Client() : this(new Packer())
    {
    }

    public Client(string addr) : this(addr, new Packer())
    {
    }

    public Client(Packer packer) : this(new Tcp(packer), packer, DEFAULT_HEARTBEAT_INTERVAL)
    {
    }

    public Client(string addr, Packer packer) : this(addr, new Tcp(packer), packer, DEFAULT_HEARTBEAT_INTERVAL)
    {
    }

    public Client(string addr, int heartbeatInterval) : this(addr, new Tcp(new Packer()), new Packer(), heartbeatInterval)
    {
    }

    public Client(string addr, Packer packer, int heartbeatInterval) : this(addr, new Tcp(packer), packer, heartbeatInterval)
    {
    }

    public Client(INetwork network, Packer packer) : this(network, packer, DEFAULT_HEARTBEAT_INTERVAL)
    {
    }

    public Client(INetwork network, Packer packer, int heartbeatInterval)
    {
        this.network = network;
        this.packer = packer;
        this.syncContext = SynchronizationContext.Current;

        if (heartbeatInterval > 0)
        {
            this.timer = new Timer(heartbeatInterval);
            this.timer.Elapsed += OnTimedEvent;
            this.heartbeatBuffer = this.packer.PackHeartbeat();
        }

        this.BindNetworkEvents();
    }

    public Client(string addr, INetwork network, Packer packer, int heartbeatInterval)
    {
        this.addr = addr;
        this.network = network;
        this.packer = packer;
        this.syncContext = SynchronizationContext.Current;

        if (heartbeatInterval > 0)
        {
            this.timer = new Timer(heartbeatInterval);
            this.timer.Elapsed += OnTimedEvent;
            this.heartbeatBuffer = this.packer.PackHeartbeat();
        }

        this.BindNetworkEvents();
    }

    // 绑定网络层事件
    private void BindNetworkEvents()
    {
        this.network.OnConnected = () =>
        {
            this.Heartbeat();
            this.timer?.Start();

            if (this.connectHandler != null)
            {
                this.Dispatch(() => this.connectHandler(this));
            }
        };

        this.network.OnDisconnected = () =>
        {
            this.timer?.Stop();

            if (this.disconnectHandler != null)
            {
                this.Dispatch(() => this.disconnectHandler(this));
            }
        };

        this.network.OnPacket = (byte[] buff) =>
        {
            try
            {
                Packet packet = this.packer.Unpack(buff);

                if (packet.IsHeartbeat)
                {
                    if (this.heartbeatHandler != null)
                    {
                        long? millisecond = packet.Millisecond;
                        this.Dispatch(() => this.heartbeatHandler(this, millisecond));
                    }
                }
                else
                {
                    if (this.receiveHandler != null && packet.Message != null)
                    {
                        Message message = (Message)packet.Message;
                        this.Dispatch(() => this.receiveHandler(this, message));
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(e);
            }
        };
    }

    // 将回调派发到主线程；若已在主线程则直接执行
    private void Dispatch(Action action)
    {
        if (this.syncContext != null && SynchronizationContext.Current != this.syncContext)
        {
            this.syncContext.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }

    // 设置连接处理器
    public void OnConnect(ConnectHandler handler)
    {
        this.connectHandler = handler;
    }

    // 设置断开连接处理器
    public void OnDisconnect(DisconnectHandler handler)
    {
        this.disconnectHandler = handler;
    }

    // 消息接收处理器
    public void OnReceive(ReceiveHandler handler)
    {
        this.receiveHandler = handler;
    }

    // 心跳消息处理器
    public void OnHeartbeat(HeartbeatHandler handler)
    {
        this.heartbeatHandler = handler;
    }

    // 定时器
    private void OnTimedEvent(object source, ElapsedEventArgs e)
    {
        this.Heartbeat();
    }

    // 连接服务器
    public bool Connect()
    {
        if (this.addr == null)
        {
            return false;
        }

        return this.Connect(this.addr);
    }

    // 连接服务器
    public bool Connect(string addr)
    {
        return this.network.Connect(addr);
    }

    // 发送数据
    public bool Send(Int32 route, Int32? seq, byte[] data)
    {
        try
        {
            byte[] buffer = this.packer.PackMessage(route, seq, data);
            return this.network.Send(buffer);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError(e);
        }

        return false;
    }

    // 发送数据
    public bool Send(Int32 route, Int32? seq, object data)
    {
        try
        {
            byte[] buffer = this.packer.PackMessage(route, seq, data);
            return this.network.Send(buffer);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError(e);
        }

        return false;
    }

    // 检测客户端是否已连接
    public bool IsConnected()
    {
        return this.network.IsConnected();
    }

    // 断开连接
    public void Disconnect()
    {
        this.network.Disconnect();
    }

    // 发送心跳包
    private void Heartbeat()
    {
        if (this.heartbeatBuffer != null)
        {
            this.network.Send(this.heartbeatBuffer);
        }
    }
}
}
