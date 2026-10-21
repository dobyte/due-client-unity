namespace Due
{
using System;
using System.Threading;
using System.Timers;
using Due.Network;
using Due.Security;

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
    private readonly System.Timers.Timer timer;
    // 心跳包
    private readonly byte[] heartbeatBuffer;
    // 主线程同步上下文：用于将后台线程回调派发回 Unity 主线程
    private readonly SynchronizationContext syncContext;
    // 会话安全配置
    private readonly SecurityOptions securityOptions;
    // 握手状态
    private HandshakeClient handshake;
    // 会话密钥
    private volatile Session session;
    // 握手超时定时器
    private System.Threading.Timer handshakeTimer;

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

    public Client(Packer packer) : this(new Tcp(packer), packer)
    {
    }

    public Client(string addr, Packer packer) : this(addr, new Tcp(packer), packer, null, DEFAULT_HEARTBEAT_INTERVAL)
    {
    }

    public Client(string addr, int heartbeatInterval) : this(addr, new Tcp(new Packer()), new Packer(), null, heartbeatInterval)
    {
    }

    public Client(string addr, Packer packer, int heartbeatInterval) : this(addr, new Tcp(packer), packer, null, heartbeatInterval)
    {
    }

    public Client(string addr, Packer packer, SecurityOptions securityOptions) : this(addr, new Tcp(packer), packer, securityOptions, DEFAULT_HEARTBEAT_INTERVAL)
    {
    }

    public Client(INetwork network, Packer packer) : this(null, network, packer, null, DEFAULT_HEARTBEAT_INTERVAL)
    {
    }

    public Client(INetwork network, Packer packer, int heartbeatInterval) : this(null, network, packer, null, heartbeatInterval)
    {
    }

    public Client(INetwork network, Packer packer, SecurityOptions securityOptions) : this(null, network, packer, securityOptions, DEFAULT_HEARTBEAT_INTERVAL)
    {
    }

    public Client(INetwork network, Packer packer, SecurityOptions securityOptions, int heartbeatInterval) : this(null, network, packer, securityOptions, heartbeatInterval)
    {
    }

    public Client(string addr, INetwork network, Packer packer) : this(addr, network, packer, null, DEFAULT_HEARTBEAT_INTERVAL)
    {
    }

    public Client(string addr, INetwork network, Packer packer, int heartbeatInterval) : this(addr, network, packer, null, heartbeatInterval)
    {
    }

    public Client(string addr, INetwork network, Packer packer, SecurityOptions securityOptions, int heartbeatInterval)
    {
        if (securityOptions != null && securityOptions.Verifier == null)
        {
            throw new ArgumentException("The verifier of the security options must not be null", nameof(securityOptions));
        }

        this.addr = addr;
        this.network = network;
        this.packer = packer;
        this.securityOptions = securityOptions;
        this.syncContext = SynchronizationContext.Current;

        if (heartbeatInterval > 0)
        {
            this.timer = new System.Timers.Timer(heartbeatInterval);
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
            this.handshake = null;
            this.session = null;

            if (this.securityOptions != null)
            {
                this.StartHandshake();
            }
            else
            {
                this.OnEstablished();
            }
        };

        this.network.OnDisconnected = () =>
        {
            this.timer?.Stop();

            this.StopHandshakeTimer();

            this.handshake = null;
            this.session = null;

            if (this.disconnectHandler != null)
            {
                this.Dispatch(() => this.disconnectHandler(this));
            }
        };

        this.network.OnPacket = (byte[] buff) =>
        {
            try
            {
                Frame frame = this.packer.UnpackFrame(buff);

                if (frame.IsHeartbeat)
                {
                    if (this.heartbeatHandler != null)
                    {
                        long? millisecond = frame.Millisecond;
                        this.Dispatch(() => this.heartbeatHandler(this, millisecond));
                    }
                }
                else if (frame.IsHandshake)
                {
                    this.HandleHandshake(frame.Payload);
                }
                else
                {
                    byte[] payload = frame.Payload;

                    if (this.securityOptions != null)
                    {
                        Session current = this.session;

                        if (current == null)
                        {
                            UnityEngine.Debug.LogError("The security session is not established");
                            return;
                        }

                        payload = current.OpenMessage(payload, Direction.ServerToClient, frame.Route, frame.Seq);
                    }

                    if (this.receiveHandler != null)
                    {
                        Message message = new Message(frame.Seq, frame.Route, this.packer.Decode(payload));
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

    // 连接就绪：启动心跳并触发连接回调；安全模式下在握手完成后调用
    private void OnEstablished()
    {
        this.Heartbeat();
        this.timer?.Start();

        if (this.connectHandler != null)
        {
            this.Dispatch(() => this.connectHandler(this));
        }
    }

    // 发起会话安全握手
    private void StartHandshake()
    {
        try
        {
            this.handshake = new HandshakeClient(this.securityOptions.Verifier, this.securityOptions.Suites);

            byte[] hello = this.handshake.Hello();

            this.StartHandshakeTimer();

            if (!this.network.Send(this.packer.PackHandshake(hello)))
            {
                throw new Exception("Send client hello failed");
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError(e);
            this.network.Disconnect();
        }
    }

    // 处理服务端握手帧
    private void HandleHandshake(byte[] payload)
    {
        HandshakeClient current = this.handshake;

        if (current == null)
        {
            UnityEngine.Debug.LogError("Unexpected handshake frame");
            return;
        }

        try
        {
            Session established = current.Handle(payload, out byte[] finished);

            // 先发布会话，再回发 Finished：服务端确认后可能立即下发加密消息
            this.session = established;

            this.handshake = null;
            this.StopHandshakeTimer();

            if (!this.network.Send(this.packer.PackHandshake(finished)))
            {
                throw new Exception("Send finished frame failed");
            }

            this.OnEstablished();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError(e);
            this.network.Disconnect();
        }
    }

    // 启动握手超时定时器
    private void StartHandshakeTimer()
    {
        int timeout = this.securityOptions.HandshakeTimeout;

        if (timeout <= 0)
        {
            return;
        }

        this.StopHandshakeTimer();
        this.handshakeTimer = new System.Threading.Timer(this.HandleHandshakeTimeout, null, timeout, Timeout.Infinite);
    }

    // 停止握手超时定时器
    private void StopHandshakeTimer()
    {
        System.Threading.Timer timer = this.handshakeTimer;

        if (timer == null)
        {
            return;
        }

        this.handshakeTimer = null;
        timer.Dispose();
    }

    // 握手超时：断开连接
    private void HandleHandshakeTimeout(object state)
    {
        if (this.session != null)
        {
            return;
        }

        UnityEngine.Debug.LogError("The security handshake timed out");
        this.network.Disconnect();
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
            if (this.securityOptions != null)
            {
                Session current = this.session;

                if (current == null)
                {
                    UnityEngine.Debug.LogWarning("The security session is not established");
                    return false;
                }

                data = current.SealMessage(data, Direction.ClientToServer, route, seq ?? 0);
            }

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
            if (this.securityOptions != null)
            {
                Session current = this.session;

                if (current == null)
                {
                    UnityEngine.Debug.LogWarning("The security session is not established");
                    return false;
                }

                byte[] payload = this.packer.Encode(data);
                payload = current.SealMessage(payload, Direction.ClientToServer, route, seq ?? 0);

                return this.network.Send(this.packer.PackMessage(route, seq, payload));
            }

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
