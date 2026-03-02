// Embedded from NativeWebSocket by Endel Dreyer, Jiri Hybek
// https://github.com/endel/NativeWebSocket | Apache 2.0 License

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using AOT;
using System.Runtime.InteropServices;
using UnityEngine;
using System.Collections;

public class MainThreadUtil : MonoBehaviour
{
    public static MainThreadUtil Instance { get; private set; }
    public static SynchronizationContext synchronizationContext { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Setup()
    {
        Instance = new GameObject("MainThreadUtil").AddComponent<MainThreadUtil>();
        synchronizationContext = SynchronizationContext.Current;
    }

    public static void Run(IEnumerator waitForUpdate)
    {
        synchronizationContext.Post(_ => Instance.StartCoroutine(waitForUpdate), null);
    }

    void Awake()
    {
        gameObject.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(gameObject);
    }
}

public class WaitForUpdate : CustomYieldInstruction
{
    public override bool keepWaiting => false;

    public MainThreadAwaiter GetAwaiter()
    {
        var awaiter = new MainThreadAwaiter();
        MainThreadUtil.Run(CoroutineWrapper(this, awaiter));
        return awaiter;
    }

    public class MainThreadAwaiter : INotifyCompletion
    {
        Action continuation;
        public bool IsCompleted { get; set; }
        public void GetResult() { }
        public void Complete()
        {
            IsCompleted = true;
            continuation?.Invoke();
        }
        void INotifyCompletion.OnCompleted(Action continuation) => this.continuation = continuation;
    }

    public static IEnumerator CoroutineWrapper(IEnumerator theWorker, MainThreadAwaiter awaiter)
    {
        yield return theWorker;
        awaiter.Complete();
    }
}

namespace NativeWebSocket
{
    public delegate void WebSocketOpenEventHandler();
    public delegate void WebSocketMessageEventHandler(byte[] data);
    public delegate void WebSocketErrorEventHandler(string errorMsg);
    public delegate void WebSocketCloseEventHandler(WebSocketCloseCode closeCode);

    public enum WebSocketCloseCode
    {
        NotSet = 0,
        Normal = 1000,
        Away = 1001,
        ProtocolError = 1002,
        UnsupportedData = 1003,
        Undefined = 1004,
        NoStatus = 1005,
        Abnormal = 1006,
        InvalidData = 1007,
        PolicyViolation = 1008,
        TooBig = 1009,
        MandatoryExtension = 1010,
        ServerError = 1011,
        TlsHandshakeFailure = 1015
    }

    public enum WebSocketState
    {
        Connecting,
        Open,
        Closing,
        Closed
    }

    public interface IWebSocket
    {
        event WebSocketOpenEventHandler OnOpen;
        event WebSocketMessageEventHandler OnMessage;
        event WebSocketErrorEventHandler OnError;
        event WebSocketCloseEventHandler OnClose;
        WebSocketState State { get; }
    }

    public static class WebSocketHelpers
    {
        public static WebSocketCloseCode ParseCloseCodeEnum(int closeCode)
        {
            if (Enum.IsDefined(typeof(WebSocketCloseCode), closeCode))
                return (WebSocketCloseCode)closeCode;
            return WebSocketCloseCode.Undefined;
        }

        public static WebSocketException GetErrorMessageFromCode(int errorCode, Exception inner)
        {
            switch (errorCode)
            {
                case -1: return new WebSocketUnexpectedException("WebSocket instance not found.", inner);
                case -2: return new WebSocketInvalidStateException("WebSocket is already connected or in connecting state.", inner);
                case -3: return new WebSocketInvalidStateException("WebSocket is not connected.", inner);
                case -4: return new WebSocketInvalidStateException("WebSocket is already closing.", inner);
                case -5: return new WebSocketInvalidStateException("WebSocket is already closed.", inner);
                case -6: return new WebSocketInvalidStateException("WebSocket is not in open state.", inner);
                case -7: return new WebSocketInvalidArgumentException("Cannot close WebSocket. An invalid code was specified or reason is too long.", inner);
                default: return new WebSocketUnexpectedException("Unknown error.", inner);
            }
        }
    }

    public class WebSocketException : Exception
    {
        public WebSocketException() { }
        public WebSocketException(string message) : base(message) { }
        public WebSocketException(string message, Exception inner) : base(message, inner) { }
    }

    public class WebSocketUnexpectedException : WebSocketException
    {
        public WebSocketUnexpectedException(string message) : base(message) { }
        public WebSocketUnexpectedException(string message, Exception inner) : base(message, inner) { }
    }

    public class WebSocketInvalidArgumentException : WebSocketException
    {
        public WebSocketInvalidArgumentException(string message) : base(message) { }
        public WebSocketInvalidArgumentException(string message, Exception inner) : base(message, inner) { }
    }

    public class WebSocketInvalidStateException : WebSocketException
    {
        public WebSocketInvalidStateException(string message) : base(message) { }
        public WebSocketInvalidStateException(string message, Exception inner) : base(message, inner) { }
    }

    public class WaitForBackgroundThread
    {
        public ConfiguredTaskAwaitable.ConfiguredTaskAwaiter GetAwaiter() =>
            Task.Run(() => { }).ConfigureAwait(false).GetAwaiter();
    }

#if UNITY_WEBGL && !UNITY_EDITOR

    public class WebSocket : IWebSocket
    {
        [DllImport("__Internal")]
        public static extern int WebSocketConnect(int instanceId);
        [DllImport("__Internal")]
        public static extern int WebSocketClose(int instanceId, int code, string reason);
        [DllImport("__Internal")]
        public static extern int WebSocketSend(int instanceId, byte[] dataPtr, int dataLength);
        [DllImport("__Internal")]
        public static extern int WebSocketSendText(int instanceId, string message);
        [DllImport("__Internal")]
        public static extern int WebSocketGetState(int instanceId);

        protected int instanceId;

        public event WebSocketOpenEventHandler OnOpen;
        public event WebSocketMessageEventHandler OnMessage;
        public event WebSocketErrorEventHandler OnError;
        public event WebSocketCloseEventHandler OnClose;

        public WebSocket(string url, Dictionary<string, string> headers = null)
        {
            if (!WebSocketFactory.isInitialized)
                WebSocketFactory.Initialize();
            instanceId = WebSocketFactory.WebSocketAllocate(url);
            WebSocketFactory.instances.Add(instanceId, this);
        }

        public WebSocket(string url, string subprotocol, Dictionary<string, string> headers = null)
        {
            if (!WebSocketFactory.isInitialized)
                WebSocketFactory.Initialize();
            instanceId = WebSocketFactory.WebSocketAllocate(url);
            WebSocketFactory.instances.Add(instanceId, this);
            WebSocketFactory.WebSocketAddSubProtocol(instanceId, subprotocol);
        }

        public WebSocket(string url, List<string> subprotocols, Dictionary<string, string> headers = null)
        {
            if (!WebSocketFactory.isInitialized)
                WebSocketFactory.Initialize();
            instanceId = WebSocketFactory.WebSocketAllocate(url);
            WebSocketFactory.instances.Add(instanceId, this);
            foreach (string subprotocol in subprotocols)
                WebSocketFactory.WebSocketAddSubProtocol(instanceId, subprotocol);
        }

        ~WebSocket() => WebSocketFactory.HandleInstanceDestroy(instanceId);

        public Task Connect()
        {
            int ret = WebSocketConnect(instanceId);
            if (ret < 0) throw WebSocketHelpers.GetErrorMessageFromCode(ret, null);
            return Task.CompletedTask;
        }

        public void CancelConnection()
        {
            if (State == WebSocketState.Open) Close(WebSocketCloseCode.Abnormal);
        }

        public Task Close(WebSocketCloseCode code = WebSocketCloseCode.Normal, string reason = null)
        {
            int ret = WebSocketClose(instanceId, (int)code, reason);
            if (ret < 0) throw WebSocketHelpers.GetErrorMessageFromCode(ret, null);
            return Task.CompletedTask;
        }

        public Task Send(byte[] data)
        {
            int ret = WebSocketSend(instanceId, data, data.Length);
            if (ret < 0) throw WebSocketHelpers.GetErrorMessageFromCode(ret, null);
            return Task.CompletedTask;
        }

        public Task SendText(string message)
        {
            int ret = WebSocketSendText(instanceId, message);
            if (ret < 0) throw WebSocketHelpers.GetErrorMessageFromCode(ret, null);
            return Task.CompletedTask;
        }

        public WebSocketState State
        {
            get
            {
                int state = WebSocketGetState(instanceId);
                if (state < 0) throw WebSocketHelpers.GetErrorMessageFromCode(state, null);
                switch (state)
                {
                    case 0: return WebSocketState.Connecting;
                    case 1: return WebSocketState.Open;
                    case 2: return WebSocketState.Closing;
                    default: return WebSocketState.Closed;
                }
            }
        }

        public void DelegateOnOpenEvent() => OnOpen?.Invoke();
        public void DelegateOnMessageEvent(byte[] data) => OnMessage?.Invoke(data);
        public void DelegateOnErrorEvent(string errorMsg) => OnError?.Invoke(errorMsg);
        public void DelegateOnCloseEvent(int closeCode) => OnClose?.Invoke(WebSocketHelpers.ParseCloseCodeEnum(closeCode));
    }

#else

    public class WebSocket : IWebSocket
    {
        public event WebSocketOpenEventHandler OnOpen;
        public event WebSocketMessageEventHandler OnMessage;
        public event WebSocketErrorEventHandler OnError;
        public event WebSocketCloseEventHandler OnClose;

        private Uri uri;
        private Dictionary<string, string> headers;
        private List<string> subprotocols;
        private ClientWebSocket m_Socket = new ClientWebSocket();

        private CancellationTokenSource m_TokenSource;
        private CancellationToken m_CancellationToken;

        private readonly object OutgoingMessageLock = new object();
        private readonly object IncomingMessageLock = new object();

        private bool isSending = false;
        private List<ArraySegment<byte>> sendBytesQueue = new List<ArraySegment<byte>>();
        private List<ArraySegment<byte>> sendTextQueue = new List<ArraySegment<byte>>();

#if UNITY_6000_0_OR_NEWER
        public bool IgnoreCertificateErrors { get; set; }
#endif

        public WebSocket(string url, Dictionary<string, string> headers = null)
        {
            uri = new Uri(url);
            this.headers = headers ?? new Dictionary<string, string>();
            subprotocols = new List<string>();
            if (uri.Scheme != "ws" && uri.Scheme != "wss")
                throw new ArgumentException("Unsupported protocol: " + uri.Scheme);
        }

        public WebSocket(string url, string subprotocol, Dictionary<string, string> headers = null)
        {
            uri = new Uri(url);
            this.headers = headers ?? new Dictionary<string, string>();
            subprotocols = new List<string> { subprotocol };
            if (uri.Scheme != "ws" && uri.Scheme != "wss")
                throw new ArgumentException("Unsupported protocol: " + uri.Scheme);
        }

        public WebSocket(string url, List<string> subprotocols, Dictionary<string, string> headers = null)
        {
            uri = new Uri(url);
            this.headers = headers ?? new Dictionary<string, string>();
            this.subprotocols = subprotocols;
            if (uri.Scheme != "ws" && uri.Scheme != "wss")
                throw new ArgumentException("Unsupported protocol: " + uri.Scheme);
        }

        public void CancelConnection() => m_TokenSource?.Cancel();

        public async Task Connect()
        {
            try
            {
                m_TokenSource = new CancellationTokenSource();
                m_CancellationToken = m_TokenSource.Token;
                m_Socket = new ClientWebSocket();

#if UNITY_6000_0_OR_NEWER
                if (IgnoreCertificateErrors)
                {
                    m_Socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                }
#endif

                foreach (var header in headers)
                    m_Socket.Options.SetRequestHeader(header.Key, header.Value);
                foreach (string subprotocol in subprotocols)
                    m_Socket.Options.AddSubProtocol(subprotocol);

                await m_Socket.ConnectAsync(uri, m_CancellationToken);
                OnOpen?.Invoke();
                await Receive();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex.Message);
                OnClose?.Invoke(WebSocketCloseCode.Abnormal);
            }
            finally
            {
                m_TokenSource?.Cancel();
                m_Socket?.Dispose();
            }
        }

        public WebSocketState State
        {
            get
            {
                switch (m_Socket.State)
                {
                    case System.Net.WebSockets.WebSocketState.Connecting: return WebSocketState.Connecting;
                    case System.Net.WebSockets.WebSocketState.Open: return WebSocketState.Open;
                    case System.Net.WebSockets.WebSocketState.CloseSent:
                    case System.Net.WebSockets.WebSocketState.CloseReceived: return WebSocketState.Closing;
                    default: return WebSocketState.Closed;
                }
            }
        }

        public Task Send(byte[] bytes) => SendMessage(sendBytesQueue, WebSocketMessageType.Binary, new ArraySegment<byte>(bytes));

        public Task SendText(string message)
        {
            var encoded = Encoding.UTF8.GetBytes(message);
            return SendMessage(sendTextQueue, WebSocketMessageType.Text, new ArraySegment<byte>(encoded, 0, encoded.Length));
        }

        private async Task SendMessage(List<ArraySegment<byte>> queue, WebSocketMessageType messageType, ArraySegment<byte> buffer)
        {
            if (buffer.Count == 0) return;
            bool sending;
            lock (OutgoingMessageLock)
            {
                sending = isSending;
                if (!isSending) isSending = true;
            }
            if (!sending)
            {
                if (!Monitor.TryEnter(m_Socket, 1000))
                {
                    await m_Socket.CloseAsync(WebSocketCloseStatus.InternalServerError, string.Empty, m_CancellationToken);
                    return;
                }
                try
                {
                    m_Socket.SendAsync(buffer, messageType, true, m_CancellationToken).Wait(m_CancellationToken);
                }
                finally
                {
                    Monitor.Exit(m_Socket);
                    lock (OutgoingMessageLock) isSending = false;
                }
                await HandleQueue(queue, messageType);
            }
            else
            {
                lock (OutgoingMessageLock) queue.Add(buffer);
            }
        }

        private async Task HandleQueue(List<ArraySegment<byte>> queue, WebSocketMessageType messageType)
        {
            var buffer = new ArraySegment<byte>();
            lock (OutgoingMessageLock)
            {
                if (queue.Count > 0) { buffer = queue[0]; queue.RemoveAt(0); }
            }
            if (buffer.Count > 0) await SendMessage(queue, messageType, buffer);
        }

        private List<byte[]> m_MessageList = new List<byte[]>();

        public void DispatchMessageQueue()
        {
            if (m_MessageList.Count == 0) return;
            List<byte[]> messageListCopy;
            lock (IncomingMessageLock)
            {
                messageListCopy = new List<byte[]>(m_MessageList);
                m_MessageList.Clear();
            }
            foreach (var msg in messageListCopy)
                OnMessage?.Invoke(msg);
        }

        public async Task Receive()
        {
            WebSocketCloseCode closeCode = WebSocketCloseCode.Abnormal;
            await new WaitForBackgroundThread();
            var buffer = new ArraySegment<byte>(new byte[8192]);
            try
            {
                while (m_Socket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    WebSocketReceiveResult result;
                    using (var ms = new MemoryStream())
                    {
                        do
                        {
                            result = await m_Socket.ReceiveAsync(buffer, m_CancellationToken);
                            ms.Write(buffer.Array, buffer.Offset, result.Count);
                        } while (!result.EndOfMessage);
                        ms.Seek(0, SeekOrigin.Begin);
                        if (result.MessageType == WebSocketMessageType.Text || result.MessageType == WebSocketMessageType.Binary)
                        {
                            lock (IncomingMessageLock) m_MessageList.Add(ms.ToArray());
                        }
                        else if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await Close();
                            closeCode = WebSocketHelpers.ParseCloseCodeEnum((int)result.CloseStatus);
                            break;
                        }
                    }
                }
            }
            catch { m_TokenSource?.Cancel(); }
            finally
            {
                await new WaitForUpdate();
                OnClose?.Invoke(closeCode);
            }
        }

        public async Task Close()
        {
            if (State == WebSocketState.Open)
                await m_Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, m_CancellationToken);
        }
    }

#endif

    public static class WebSocketFactory
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        public static Dictionary<int, WebSocket> instances = new Dictionary<int, WebSocket>();
        public delegate void OnOpenCallback(int instanceId);
        public delegate void OnMessageCallback(int instanceId, IntPtr msgPtr, int msgSize);
        public delegate void OnErrorCallback(int instanceId, IntPtr errorPtr);
        public delegate void OnCloseCallback(int instanceId, int closeCode);

        [DllImport("__Internal")] public static extern int WebSocketAllocate(string url);
        [DllImport("__Internal")] public static extern void WebSocketAddSubProtocol(int instanceId, string subprotocol);
        [DllImport("__Internal")] public static extern void WebSocketFree(int instanceId);
        [DllImport("__Internal")] public static extern void WebSocketSetOnOpen(OnOpenCallback callback);
        [DllImport("__Internal")] public static extern void WebSocketSetOnMessage(OnMessageCallback callback);
        [DllImport("__Internal")] public static extern void WebSocketSetOnError(OnErrorCallback callback);
        [DllImport("__Internal")] public static extern void WebSocketSetOnClose(OnCloseCallback callback);

        public static bool isInitialized = false;

        public static void Initialize()
        {
            WebSocketSetOnOpen(DelegateOnOpenEvent);
            WebSocketSetOnMessage(DelegateOnMessageEvent);
            WebSocketSetOnError(DelegateOnErrorEvent);
            WebSocketSetOnClose(DelegateOnCloseEvent);
            isInitialized = true;
        }

        public static void HandleInstanceDestroy(int instanceId)
        {
            instances.Remove(instanceId);
            WebSocketFree(instanceId);
        }

        [MonoPInvokeCallback(typeof(OnOpenCallback))]
        public static void DelegateOnOpenEvent(int instanceId)
        {
            if (instances.TryGetValue(instanceId, out WebSocket instanceRef))
                instanceRef.DelegateOnOpenEvent();
        }

        [MonoPInvokeCallback(typeof(OnMessageCallback))]
        public static void DelegateOnMessageEvent(int instanceId, IntPtr msgPtr, int msgSize)
        {
            if (instances.TryGetValue(instanceId, out WebSocket instanceRef))
            {
                byte[] msg = new byte[msgSize];
                Marshal.Copy(msgPtr, msg, 0, msgSize);
                instanceRef.DelegateOnMessageEvent(msg);
            }
        }

        [MonoPInvokeCallback(typeof(OnErrorCallback))]
        public static void DelegateOnErrorEvent(int instanceId, IntPtr errorPtr)
        {
            if (instances.TryGetValue(instanceId, out WebSocket instanceRef))
            {
                string errorMsg = Marshal.PtrToStringAuto(errorPtr);
                instanceRef.DelegateOnErrorEvent(errorMsg);
            }
        }

        [MonoPInvokeCallback(typeof(OnCloseCallback))]
        public static void DelegateOnCloseEvent(int instanceId, int closeCode)
        {
            if (instances.TryGetValue(instanceId, out WebSocket instanceRef))
                instanceRef.DelegateOnCloseEvent(closeCode);
        }
#else
        public static bool isInitialized => true;
        public static WebSocket CreateInstance(string url) => new WebSocket(url);
#endif
    }
}
