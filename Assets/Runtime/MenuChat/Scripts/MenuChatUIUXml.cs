using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if !UNITY_WEBGL || UNITY_EDITOR
#if UNITY_6000_0_OR_NEWER
using NativeWebSocket; // Unity 6+ built-in
#else
using NativeWebSocket; // From com.endel.nativewebsocket package (2021.3, 2022, 2023, etc.)
#endif
#endif

namespace CardChat.UI
{
    /// <summary>
    /// Abstraction for local LLM chat. Implemented by MenuChatLocalLLM to isolate LLMUnity dependencies.
    /// MenuChatUIUXml depends on this interface only, so it compiles when MenuChatLocalLLM/LLMUnity is absent.
    /// </summary>
    public interface IMenuChatLocalLLM
    {
        bool IsAvailable { get; }
        Task<string> ChatAsync(string message, Action<string> onPartial, Action onComplete);
    }

    /// <summary>
    /// MenuChat implemented with UI Toolkit (.uxml + .uss).
    /// Chat button (talk-bubble icon) bottom-left with unread badge; toggle-inbox when open; help button; markdown hyperlinks; info popup on link hover.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MenuChatUIUXml : MonoBehaviour
    {
        public enum ConnectionMode
        {
            LocalLLM,
            RemoteWebSocket
        }

        private const string ChatButtonIcon = "💬";
        private const string HelpButtonIcon = "?";

        private static readonly string HelpMessageBody = @"**Chat Help** 📖

• **Send:** Type and press Enter or click Send.
• **Help:** Type ""help"" or click the ? button — this message is local (not sent to the server).
• **Links:** Use [linkname] or [linkname](url). Double brackets [[button]] show as buttons at the bottom of the message.
• **Web links:** Links starting with https:// open in your browser.

[Documentation](https://github.com/Magi-AGI/cardcore)";

        [Header("Connection Settings")]
        public ConnectionMode connectionMode = ConnectionMode.LocalLLM;

        [Header("Local LLM Settings")]
        [Tooltip("Assign GameObject with MenuChatLocalLLM component for local chat. Isolates LLMUnity from this script.")]
        [SerializeField] private GameObject localChatProviderObject;

        private IMenuChatLocalLLM LocalChatProvider => localChatProviderObject != null ? localChatProviderObject.GetComponent<IMenuChatLocalLLM>() : null;

        [Header("Remote WebSocket Settings")]
        public string websocketUrl = "ws://52.9.223.25:8000/ws/v1/chat/completions";
        public string websocketUrlLocal = "ws://localhost:8000/ws/v1/chat/completions";
        public bool connectOnStart = true;
        public float reconnectDelay = 3f;
        public int maxReconnectAttempts = 5;
        public bool autoReconnect = true;

        [Header("Audio")]
        public AudioClip chat_audio;
        [Tooltip("Optional AudioSource for playing chat SFX. If unassigned, chat_audio is ignored.")]
        [SerializeField] private AudioSource audioSource;

        [Header("Chat Log")]
        public int max_chat_log_lines = 50;
        public string chat_log_player_name = "You";

        [Header("Hyperlinks & Help")]
        public HyperlinkDictionary hyperlinkDictionary;
        public string helpMessageUrl = "https://github.com/Magi-AGI/cardcore";

        [Header("Debug")]
        public bool logMessages = true;

        [Header("Visibility")]
        [Tooltip("When true, chat starts hidden. Use OpenFromExternal() from a uGUI button to show.")]
        public bool hideOnStart = true;

        [Header("Passthrough to uGUI")]
        [Tooltip("When true, sets PanelSettings sort order to -1 when chat is closed (so uGUI behind receives clicks) and 0 when open. Use OpenFromExternal() from a uGUI button to reopen.")]
        public bool disableWhenChatClosed = true;

        private UIDocument uiDocument;
        private VisualElement root;
        private VisualElement chatButtonWrapper;
        private Button chatButton;
        private Label chatButtonBadge;
        private Button chatToggleInbox;
        private VisualElement chatBox;
        private ScrollView chatScroll;
        private VisualElement chatLogContent;
        private TextField chatInput;
        private Button helpButton;
        private Button sendButton;
        private VisualElement infoPopup;
        private Label infoPopupTitle;
        private Label infoPopupText;
        private VisualElement chatBubble;
        private Label chatBubbleText;
        private VisualElement passthroughOverlay;

        private int unreadCount;

        private string chat_msg;
        private float chat_timer;
        private bool chat_timer_started;
        private bool is_processing;
        private readonly List<VisualElement> chat_log_lines = new List<VisualElement>();

#if !UNITY_WEBGL || UNITY_EDITOR
#if UNITY_6000_0_OR_NEWER || UNITY_2021_3_OR_NEWER
        private WebSocket websocket;
        private bool is_connecting;
        private int reconnect_attempts;
        private readonly Queue<string> message_queue = new Queue<string>();
        private volatile bool pending_reconnect;
        private bool is_reconnect_scheduled;
        private bool connection_shutting_down;
        private bool connection_failure_logged;
#endif
#endif

        private static readonly List<MenuChatUIUXml> ui_list = new List<MenuChatUIUXml>();

        private void Awake()
        {
            ui_list.Add(this);
            uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            if (uiDocument?.rootVisualElement == null)
                return;
            BindUI();
        }

        private void OnDestroy()
        {
            ui_list.Remove(this);
#if !UNITY_WEBGL || UNITY_EDITOR
#if UNITY_6000_0_OR_NEWER || UNITY_2021_3_OR_NEWER
            DisconnectWebSocket();
#endif
#endif
        }

        private void BindUI()
        {
            root = uiDocument.rootVisualElement;
            chatButtonWrapper = root.Q<VisualElement>("chat-button-wrapper");
            chatButton = root.Q<Button>("chat-button");
            chatButtonBadge = root.Q<Label>("chat-button-badge");
            chatToggleInbox = root.Q<Button>("chat-toggle-inbox");
            chatBox = root.Q<VisualElement>("chat-box");
            chatScroll = root.Q<ScrollView>("chat-scroll");
            chatLogContent = root.Q<VisualElement>("chat-log-content");
            chatInput = root.Q<TextField>("chat-input");
            helpButton = root.Q<Button>("help-button");
            sendButton = root.Q<Button>("send-button");
            infoPopup = root.Q<VisualElement>("info-popup");
            infoPopupTitle = root.Q<Label>("info-popup-title");
            infoPopupText = root.Q<Label>("info-popup-text");
            chatBubble = root.Q<VisualElement>("chat-bubble");
            chatBubbleText = root.Q<Label>("chat-bubble-text");
            passthroughOverlay = root.Q<VisualElement>("passthrough-overlay");

            if (passthroughOverlay != null)
            {
                passthroughOverlay.pickingMode = PickingMode.Ignore;
                passthroughOverlay.StretchToParentSize();
            }
            if (root != null)
            {
                root.pickingMode = PickingMode.Ignore;
                root.StretchToParentSize();
            }
            if (chatButtonWrapper != null)
                chatButtonWrapper.pickingMode = PickingMode.Position;
            if (chatBox != null)
            {
                chatBox.pickingMode = PickingMode.Position;
                chatBox.style.display = DisplayStyle.None;
            }
            if (chatBubble != null)
                chatBubble.pickingMode = PickingMode.Ignore;
            if (infoPopup != null)
            {
                infoPopup.pickingMode = PickingMode.Position;
                infoPopup.style.display = DisplayStyle.None;
            }

            if (chatButton != null)
            {
                chatButton.text = ChatButtonIcon;
                chatButton.clicked -= OnChatButtonClick;
                chatButton.clicked += OnChatButtonClick;
            }
            if (chatToggleInbox != null)
            {
                chatToggleInbox.text = ChatButtonIcon;
                chatToggleInbox.clicked -= OnChatToggleInboxClick;
                chatToggleInbox.clicked += OnChatToggleInboxClick;
            }
            if (helpButton != null)
            {
                helpButton.text = HelpButtonIcon;
                helpButton.clicked -= OnHelpClick;
                helpButton.clicked += OnHelpClick;
            }
            UpdateBadge();

            if (sendButton != null)
            {
                sendButton.clicked -= OnSendClick;
                sendButton.clicked += OnSendClick;
            }

            if (chatInput != null)
                chatInput.RegisterCallback<KeyDownEvent>(OnChatInputKeyDown);
        }

        private void OnChatToggleInboxClick()
        {
            HideChatBox();
        }

        private void OnHelpClick()
        {
            ShowHelpMessage();
        }

        private void UpdateBadge()
        {
            if (chatButtonBadge == null) return;
            if (unreadCount <= 0)
            {
                chatButtonBadge.style.display = DisplayStyle.None;
                chatButtonBadge.text = "";
            }
            else
            {
                chatButtonBadge.style.display = DisplayStyle.Flex;
                chatButtonBadge.text = unreadCount > 99 ? "99+" : unreadCount.ToString();
            }
        }

        private void OnChatButtonClick()
        {
            if (chatBox == null) return;
            if (chatBox.style.display == DisplayStyle.Flex)
            {
                HideChatBox();
            }
            else
            {
                ShowChatBox();
            }
        }

        private void SetPanelSortOrder(int order)
        {
            if (!disableWhenChatClosed || uiDocument?.panelSettings == null) return;
            uiDocument.panelSettings.sortingOrder = order;
        }

        private void HideChatBox()
        {
            if (chatBox == null) return;
            TrySendFromInput();
            chatBox.style.display = DisplayStyle.None;
            SetPanelSortOrder(-1);
        }

        private void ShowChatBox()
        {
            if (chatBox == null) return;
            chatBox.style.display = DisplayStyle.Flex;
            SetPanelSortOrder(0);
            unreadCount = 0;
            UpdateBadge();
            chatInput?.Focus();
        }

        private void OnSendClick()
        {
            TrySendFromInput();
        }

        private void TrySendFromInput()
        {
            if (chatInput == null || is_processing) return;
            string text = chatInput.value?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            if (string.Equals(text, "help", StringComparison.OrdinalIgnoreCase))
            {
                chatInput.value = "";
                ShowHelpMessage();
                return;
            }
            SendChat(text);
            chatInput.value = "";
            HideChatBox();
        }

        private void ShowHelpMessage()
        {
            AddToChatLog(HelpMessageBody, false, isStatus: true);
            ShowChatBox();
        }

        private void OnChatInputKeyDown(KeyDownEvent ev)
        {
            if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter)
            {
                ev.StopPropagation();
                if (ev.shiftKey)
                    return;
                TrySendFromInput();
            }
        }

        private void Start()
        {
            if (uiDocument?.rootVisualElement != null && root == null)
                BindUI();

            if (hideOnStart)
            {
                if (chatBox != null)
                    chatBox.style.display = DisplayStyle.None;
                SetPanelSortOrder(-1);
            }
            else if (disableWhenChatClosed)
            {
                SetPanelSortOrder(0);
            }

            if (connectionMode == ConnectionMode.LocalLLM)
                InitializeLocalLLM();
            else if (connectionMode == ConnectionMode.RemoteWebSocket && connectOnStart)
#if !UNITY_WEBGL || UNITY_EDITOR
#if UNITY_6000_0_OR_NEWER || UNITY_2021_3_OR_NEWER
                StartCoroutine(ConnectToWebSocket());
#endif
#else
                { }
#endif
        }

        private void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
#if UNITY_6000_0_OR_NEWER || UNITY_2021_3_OR_NEWER
            if (connectionMode == ConnectionMode.RemoteWebSocket && websocket != null)
                websocket.DispatchMessageQueue();

            int processed = 0;
            while (message_queue.Count > 0 && processed < 10)
            {
                ProcessWebSocketMessage(message_queue.Dequeue());
                processed++;
            }

            if (connectionMode == ConnectionMode.RemoteWebSocket && pending_reconnect && !is_reconnect_scheduled && !is_connecting && reconnect_attempts < maxReconnectAttempts)
            {
                pending_reconnect = false;
                is_reconnect_scheduled = true;
                StartCoroutine(ReconnectWithDelay());
            }
            else if (connectionMode == ConnectionMode.RemoteWebSocket && reconnect_attempts >= maxReconnectAttempts && pending_reconnect)
            {
                pending_reconnect = false;
                AddConnectionStatusToChatLog("Chat unable to connect.");
                connection_failure_logged = true;
            }
#endif
#endif

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current[Key.Enter].wasPressedThisFrame)
#else
            if (Input.GetKeyDown(KeyCode.Return))
#endif
            {
                if (chatBox != null && chatBox.style.display == DisplayStyle.Flex)
                    HideChatBox();
                else if (chatBox != null)
                    ShowChatBox();
            }

            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                chat_timer_started = true;

            if (chat_timer_started)
            {
                chat_timer += Time.deltaTime;
                if (chat_timer > 5f)
                    chat_msg = null;
            }

            RefreshChatBubble();
        }

        private void InitializeLocalLLM()
        {
            if (LocalChatProvider == null || !LocalChatProvider.IsAvailable)
                Debug.LogWarning("[MenuChatUIUXml] Local chat provider (MenuChatLocalLLM) is not assigned or available for LocalLLM mode.");
        }

#if !UNITY_WEBGL || UNITY_EDITOR
#if UNITY_6000_0_OR_NEWER || UNITY_2021_3_OR_NEWER
        private IEnumerator ConnectToWebSocket()
        {
            if (is_connecting) yield break;
            connection_shutting_down = false;
            is_connecting = true;
            reconnect_attempts++;

            if (websocket != null)
            {
                connection_shutting_down = true;
                websocket.OnOpen -= HandleWsOnOpen;
                websocket.OnError -= HandleWsOnError;
                websocket.OnClose -= HandleWsOnClose;
                websocket.OnMessage -= HandleWsOnMessage;
                websocket.Close();
                websocket = null;
                yield return new WaitForSeconds(0.5f);
                if (this == null) yield break;
                connection_shutting_down = false;
            }

            try
            {
                websocket = new WebSocket(websocketUrl);
#if UNITY_6000_0_OR_NEWER
                websocket.IgnoreCertificateErrors = true;
#endif
                websocket.OnOpen += HandleWsOnOpen;
                websocket.OnError += HandleWsOnError;
                websocket.OnClose += HandleWsOnClose;
                websocket.OnMessage += HandleWsOnMessage;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MenuChatUIUXml] Error setting up WebSocket: {e.Message}");
                is_connecting = false;
                is_reconnect_scheduled = false;
                if (autoReconnect && reconnect_attempts < maxReconnectAttempts)
                    pending_reconnect = true;
                yield break;
            }

            yield return websocket.Connect();
            if (this == null) yield break;
            if (websocket != null && websocket.State != WebSocketState.Open)
            {
                if (autoReconnect && reconnect_attempts < maxReconnectAttempts)
                    pending_reconnect = true;
                else
                {
                    AddConnectionStatusToChatLog("Chat unable to connect.");
                    connection_failure_logged = true;
                }
            }

            is_connecting = false;
            is_reconnect_scheduled = false;
        }
#endif

#if UNITY_6000_0_OR_NEWER || UNITY_2021_3_OR_NEWER
        private void HandleWsOnOpen()
        {
            if (this == null || connection_shutting_down) return;
            reconnect_attempts = 0;
            connection_failure_logged = false;
        }

        private void HandleWsOnError(string e)
        {
            if (this == null || connection_shutting_down) return;
            if (autoReconnect && reconnect_attempts < maxReconnectAttempts)
                pending_reconnect = true;
        }

        private void HandleWsOnClose(WebSocketCloseCode code)
        {
            if (this == null || connection_shutting_down) return;
            if (autoReconnect && reconnect_attempts < maxReconnectAttempts)
                pending_reconnect = true;
        }

        private void HandleWsOnMessage(byte[] bytes)
        {
            if (this == null || connection_shutting_down) return;
            message_queue.Enqueue(System.Text.Encoding.UTF8.GetString(bytes));
        }
#endif

#if UNITY_6000_0_OR_NEWER || UNITY_2021_3_OR_NEWER
        private IEnumerator ReconnectWithDelay()
        {
            yield return new WaitForSeconds(reconnectDelay);
            if (this == null) yield break;
            is_reconnect_scheduled = false;
            StartCoroutine(ConnectToWebSocket());
        }
#endif

#if UNITY_6000_0_OR_NEWER || UNITY_2021_3_OR_NEWER
        private void ProcessWebSocketMessage(string message)
        {
            try
            {
                string response_text = null;
                if (message.Trim().StartsWith("{"))
                {
                    var errorMatch = Regex.Match(message, @"""error""\s*:\s*""([^""]*)""");
                    if (errorMatch.Success)
                    {
                        is_processing = false;
                        return;
                    }
                    var contentMatch = Regex.Match(message, @"""content""\s*:\s*""((?:[^""\\]|\\.)*)""");
                    if (contentMatch.Success)
                        response_text = DecodeJsonString(contentMatch.Groups[1].Value);
                }
                if (!string.IsNullOrEmpty(response_text))
                    OnReceiveMessage(response_text);
                is_processing = false;
            }
            catch
            {
                is_processing = false;
            }
        }
#endif

#if UNITY_6000_0_OR_NEWER || UNITY_2021_3_OR_NEWER
        private void SendWebSocketMessage(string message)
        {
            if (websocket == null || websocket.State != WebSocketState.Open)
            {
                if (!connection_failure_logged)
                {
                    connection_failure_logged = true;
                    AddConnectionStatusToChatLog("Chat unable to connect.");
                }
                is_processing = false;
                return;
            }
            is_processing = true;
            try
            {
                websocket.SendText($"{{\"message\": \"{EscapeJsonString(message)}\"}}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[MenuChatUIUXml] Send error: {e.Message}");
                is_processing = false;
            }
        }
#endif

        private static string EscapeJsonString(string text)
        {
            return Regex.Replace(text, @"[\u0000-\u001F\\""\/]", m =>
            {
                char c = m.Value[0];
                switch (c)
                {
                    case '\\': return "\\\\";
                    case '"': return "\\\"";
                    case '\n': return "\\n";
                    case '\r': return "\\r";
                    case '\t': return "\\t";
                    default: return $"\\u{(int)c:X4}";
                }
            });
        }

        /// <summary>Prepare message for display: unescape \\uXXXX, normalize bullet newlines.</summary>
        private static string PrepareMessageForDisplay(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = UnescapeUnicodeInMessage(text);
            return NormalizeBulletNewlines(text);
        }

        /// <summary>Unescape literal \uXXXX and \uXXXX\uYYYY sequences in any string (e.g. from Local LLM).</summary>
        private static string UnescapeUnicodeInMessage(string text)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains("\\u")) return text;
            return Regex.Replace(text, @"\\u([0-9A-Fa-f]{4})(\\u([0-9A-Fa-f]{4}))?", m =>
            {
                int hi = Convert.ToInt32(m.Groups[1].Value, 16);
                if (m.Groups[2].Success)
                {
                    int lo = Convert.ToInt32(m.Groups[3].Value, 16);
                    if (hi >= 0xD800 && hi <= 0xDBFF && lo >= 0xDC00 && lo <= 0xDFFF)
                        return char.ConvertFromUtf32(((hi - 0xD800) << 10) + (lo - 0xDC00) + 0x10000);
                }
                return ((char)hi).ToString();
            });
        }

        /// <summary>Normalize markdown: merge bullet chars that are on their own line with the following text; use • for asterisk bullets so they aren't parsed as italic.</summary>
        private static string NormalizeBulletNewlines(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = Regex.Replace(text, @"(\r?\n)(\s*[-*•]\s*)(\r?\n)(\s*)", "$1$2 ");
            text = Regex.Replace(text, @"^(\s*[-*•]\s*)(\r?\n)(\s*)", "$1 ");
            text = Regex.Replace(text, @"(\r?\n)(\s*)\*\s+", "$1$2• ");
            text = Regex.Replace(text, @"^(\s*)\*\s+", "$1• ");
            return text;
        }

        /// <summary>Decode JSON string escape sequences including \\n, \\\", \\uXXXX, and surrogate pairs \\uXXXX\\uYYYY.</summary>
        private static string DecodeJsonString(string escaped)
        {
            if (string.IsNullOrEmpty(escaped)) return escaped;
            var sb = new System.Text.StringBuilder(escaped.Length);
            for (int i = 0; i < escaped.Length; i++)
            {
                if (escaped[i] != '\\')
                {
                    sb.Append(escaped[i]);
                    continue;
                }
                if (i + 1 >= escaped.Length) { sb.Append('\\'); continue; }
                char next = escaped[i + 1];
                switch (next)
                {
                    case 'n': sb.Append('\n'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    case '"': sb.Append('"'); i++; break;
                    case '\\': sb.Append('\\'); i++; break;
                    case '/': sb.Append('/'); i++; break;
                    case 'u':
                        i++;
                        if (i + 4 > escaped.Length) { sb.Append('\\').Append(next); break; }
                        int hi = 0;
                        if (!int.TryParse(escaped.Substring(i, 4), System.Globalization.NumberStyles.HexNumber, null, out hi))
                        { sb.Append('\\').Append(next); break; }
                        i += 4;
                        if (hi >= 0xD800 && hi <= 0xDBFF && i + 6 <= escaped.Length && escaped[i] == '\\' && escaped[i + 1] == 'u')
                        {
                            int lo = 0;
                            if (int.TryParse(escaped.Substring(i + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out lo) && lo >= 0xDC00 && lo <= 0xDFFF)
                            {
                                sb.Append(char.ConvertFromUtf32(((hi - 0xD800) << 10) + (lo - 0xDC00) + 0x10000));
                                i += 5;
                                break;
                            }
                        }
                        sb.Append((char)hi);
                        break;
                    default: sb.Append('\\').Append(next); i++; break;
                }
            }
            return sb.ToString();
        }

#if UNITY_6000_0_OR_NEWER || UNITY_2021_3_OR_NEWER
        private void DisconnectWebSocket()
        {
            connection_shutting_down = true;
            pending_reconnect = false;
            if (websocket != null)
            {
                websocket.OnOpen -= HandleWsOnOpen;
                websocket.OnError -= HandleWsOnError;
                websocket.OnClose -= HandleWsOnClose;
                websocket.OnMessage -= HandleWsOnMessage;
                websocket.Close();
                websocket = null;
            }
        }
#endif
#endif

        public void SendChat(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;
            bool is_system = msg.StartsWith("__SYSTEM__:");
            string display_msg = is_system ? msg.Substring("__SYSTEM__:".Length) : msg;

            if (connectionMode == ConnectionMode.LocalLLM && (LocalChatProvider == null || !LocalChatProvider.IsAvailable))
            {
                AddConnectionStatusToChatLog("Chat unable to connect (no LLM configured).");
                return;
            }

            if (!is_system)
                AddToChatLog(display_msg, true);

            if (connectionMode == ConnectionMode.LocalLLM)
                SendLocalLLMMessage(display_msg);
            else if (connectionMode == ConnectionMode.RemoteWebSocket)
#if !UNITY_WEBGL || UNITY_EDITOR
#if UNITY_6000_0_OR_NEWER || UNITY_2021_3_OR_NEWER
                SendWebSocketMessage(display_msg);
#endif
#else
                { }
#endif
        }

        private async void SendLocalLLMMessage(string message)
        {
            var provider = LocalChatProvider;
            if (provider == null || !provider.IsAvailable) return;
            is_processing = true;
            try
            {
                DisplayMessage(message, true);
                string response = await provider.ChatAsync(message, partial => DisplayMessage(partial, false), () => is_processing = false);
                if (response != null)
                {
                    DisplayMessage(response, false);
                    AddToChatLog(response, false);
                    ShowChatBoxAndLog();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MenuChatUIUXml] LLM error: {e.Message}");
                is_processing = false;
            }
        }

        private void OnReceiveMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            DisplayMessage(message, false);
            AddToChatLog(message, false);
            if (chatBox != null && chatBox.style.display != DisplayStyle.Flex)
            {
                unreadCount++;
                UpdateBadge();
            }
            ShowChatBoxAndLog();
        }

        private void DisplayMessage(string msg, bool is_user)
        {
            chat_msg = PrepareMessageForDisplay(msg);
            chat_timer = 0f;
            chat_timer_started = false;
            if (chat_audio != null && !is_user && audioSource != null)
                audioSource.PlayOneShot(chat_audio);
        }

        private void RefreshChatBubble()
        {
            if (chatBubbleText == null) return;
            if (string.IsNullOrEmpty(chat_msg))
            {
                chatBubbleText.style.display = DisplayStyle.None;
                if (chatBubble != null) chatBubble.style.display = DisplayStyle.None;
                return;
            }
            chatBubbleText.text = chat_msg;
            chatBubbleText.style.display = DisplayStyle.Flex;
            chatBubbleText.style.color = Color.white;
            if (chatBubble != null) chatBubble.style.display = DisplayStyle.Flex;
        }

        private void AddConnectionStatusToChatLog(string message)
        {
            AddToChatLog(message, false, isStatus: true);
        }

        private void AddToChatLog(string msg, bool is_user, bool isStatus = false)
        {
            if (string.IsNullOrWhiteSpace(msg) || chatLogContent == null) return;
            msg = PrepareMessageForDisplay(msg);

            string senderName = isStatus ? "System" : (is_user ? (string.IsNullOrWhiteSpace(chat_log_player_name) ? "You" : chat_log_player_name) : "AI");

            var line = new VisualElement();
            line.AddToClassList("chat-message-line");
            if (isStatus)
                line.AddToClassList("chat-message-line--status");
            else if (is_user)
                line.AddToClassList("chat-message-line--user");
            else
                line.AddToClassList("chat-message-line--assistant");

            var sender = new Label(senderName);
            sender.AddToClassList("chat-message-sender");
            if (!is_user && !isStatus) sender.AddToClassList("chat-message-sender--ai");
            line.Add(sender);

            var textContainer = new VisualElement();
            textContainer.AddToClassList("chat-message-text-container");
            textContainer.style.flexDirection = FlexDirection.Row;
            textContainer.style.flexWrap = Wrap.Wrap;
            textContainer.style.alignItems = Align.FlexStart;
            line.Add(textContainer);
            BuildMessageContent(textContainer, msg, line);

            chatLogContent.Add(line);
            chat_log_lines.Add(line);

            while (chat_log_lines.Count > max_chat_log_lines)
            {
                var old = chat_log_lines[0];
                chat_log_lines.RemoveAt(0);
                old.RemoveFromHierarchy();
            }

            if (chatScroll != null)
                chatScroll.schedule.Execute(() =>
                {
                    if (chatScroll != null && chatScroll.contentContainer != null)
                    {
                        float contentH = chatScroll.contentContainer.layout.height;
                        float viewportH = chatScroll.contentViewport.layout.height;
                        chatScroll.scrollOffset = new Vector2(0, Mathf.Max(0, contentH - viewportH));
                    }
                }).StartingIn(0);
        }

        private struct LinkMatch
        {
            public int Start, Length;
            public string Text, Url;
            public bool IsButton;
        }

        private static List<LinkMatch> ParseLinkMatches(string msg)
        {
            var list = new List<LinkMatch>();
            if (string.IsNullOrEmpty(msg)) return list;
            var doubleBracket = new Regex(@"\[\[([^\[\]\(\)]+)\]\](?:\(([^)]+)\))?");
            var singleBracket = new Regex(@"\[([^\[\]\(\)]+)\](?:\(([^)]+)\))?");
            foreach (Match m in doubleBracket.Matches(msg))
            {
                string text = m.Groups[1].Value;
                string url = m.Groups[2].Success ? m.Groups[2].Value : null;
                list.Add(new LinkMatch { Start = m.Index, Length = m.Length, Text = text, Url = url, IsButton = true });
            }
            foreach (Match m in singleBracket.Matches(msg))
            {
                bool insideDouble = false;
                foreach (var l in list)
                    if (m.Index >= l.Start && m.Index < l.Start + l.Length) { insideDouble = true; break; }
                if (insideDouble) continue;
                string text = m.Groups[1].Value;
                string url = m.Groups[2].Success ? m.Groups[2].Value : null;
                list.Add(new LinkMatch { Start = m.Index, Length = m.Length, Text = text, Url = url, IsButton = false });
            }
            list.Sort((a, b) => a.Start.CompareTo(b.Start));
            return list;
        }

        private string ResolveLinkUrl(string term)
        {
            if (hyperlinkDictionary != null && hyperlinkDictionary.TryGetEntry(term, out var e))
                return string.IsNullOrEmpty(e.url) ? null : e.url;
            return null;
        }

        /// <summary>Add content with period-newline breaks (each ".\n" creates a new line) and bullet-line indentation.</summary>
        private void AddMarkdownContentWithPeriodBreaks(VisualElement container, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var periodParts = Regex.Split(text, @"(?<=\.)\s*\r?\n");
            for (int i = 0; i < periodParts.Length; i++)
            {
                if (i > 0)
                    AddLineBreak(container);
                AddMarkdownSegmentWithBulletLines(container, periodParts[i]);
            }
        }

        private void AddLineBreak(VisualElement container)
        {
            var br = new VisualElement();
            br.AddToClassList("chat-message-line-break");
            br.style.width = Length.Percent(100);
            br.style.height = 0;
            br.style.flexShrink = 0;
            container.Add(br);
        }

        /// <summary>Process segment line-by-line; lines starting with • get indented.</summary>
        private void AddMarkdownSegmentWithBulletLines(VisualElement container, string segment)
        {
            if (string.IsNullOrEmpty(segment)) return;
            var lines = Regex.Split(segment, @"\r?\n");
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                    AddLineBreak(container);

                var line = lines[i];
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("• ") || trimmed.StartsWith("•\t"))
                {
                    var bulletWrap = new VisualElement();
                    bulletWrap.AddToClassList("chat-message-bullet-line");
                    bulletWrap.style.flexDirection = FlexDirection.Row;
                    bulletWrap.style.flexWrap = Wrap.Wrap;
                    bulletWrap.style.alignItems = Align.FlexStart;
                    bulletWrap.style.minWidth = 0;
                    AddMarkdownSegment(bulletWrap, line);
                    container.Add(bulletWrap);
                }
                else if (TryAddHeaderLine(container, line))
                {
                    /* header added */
                }
                else
                {
                    AddMarkdownSegment(container, line);
                }
            }
        }

        /// <summary>If line is a markdown header (# ## ### etc), add styled header and return true.</summary>
        private bool TryAddHeaderLine(VisualElement container, string line)
        {
            var trimmed = line.TrimStart();
            int hashCount = 0;
            while (hashCount < trimmed.Length && trimmed[hashCount] == '#')
                hashCount++;
            if (hashCount < 1 || hashCount > 6) return false;
            int spaceIdx = hashCount;
            while (spaceIdx < trimmed.Length && trimmed[spaceIdx] == ' ')
                spaceIdx++;
            string headerText = trimmed.Substring(spaceIdx);
            if (string.IsNullOrEmpty(headerText)) return false;

            var headerLabel = new Label(headerText);
            headerLabel.AddToClassList("chat-message-header");
            headerLabel.AddToClassList($"chat-message-header--h{hashCount}");
            headerLabel.style.whiteSpace = WhiteSpace.Normal;
            container.Add(headerLabel);
            return true;
        }

        /// <summary>Append UXML-styled elements for a segment of text, parsing **bold**, *italic*, and `code`.</summary>
        private static void AddMarkdownSegment(VisualElement container, string segment)
        {
            if (string.IsNullOrEmpty(segment))
                return;
            int i = 0;
            while (i < segment.Length)
            {
                int nextBold = segment.IndexOf("**", i, StringComparison.Ordinal);
                int nextItalic = segment.IndexOf('*', i);
                if (nextItalic >= 0 && nextItalic < segment.Length - 1 && segment[nextItalic + 1] == '*')
                    nextItalic = -1;
                int nextCode = segment.IndexOf('`', i);
                int pick = -1;
                char style = '\0';
                if (nextBold >= 0 && (pick < 0 || nextBold < pick)) { pick = nextBold; style = 'b'; }
                if (nextItalic >= 0 && (pick < 0 || nextItalic < pick)) { pick = nextItalic; style = 'i'; }
                if (nextCode >= 0 && (pick < 0 || nextCode < pick)) { pick = nextCode; style = 'c'; }
                if (pick < 0)
                {
                    var plain = new Label(segment.Substring(i));
                    plain.AddToClassList("chat-message-text");
                    plain.style.whiteSpace = WhiteSpace.Normal;
                    container.Add(plain);
                    return;
                }
                if (pick > i)
                {
                    var plain = new Label(segment.Substring(i, pick - i));
                    plain.AddToClassList("chat-message-text");
                    plain.style.whiteSpace = WhiteSpace.Normal;
                    container.Add(plain);
                }
                if (style == 'b')
                {
                    int end = segment.IndexOf("**", pick + 2, StringComparison.Ordinal);
                    if (end < 0) end = segment.Length;
                    else end += 2;
                    var lbl = new Label(segment.Substring(pick + 2, end - pick - 4));
                    lbl.AddToClassList("chat-message-bold");
                    lbl.style.whiteSpace = WhiteSpace.Normal;
                    container.Add(lbl);
                    i = end;
                }
                else if (style == 'i')
                {
                    int end = pick + 1;
                    while (end < segment.Length)
                    {
                        int star = segment.IndexOf('*', end);
                        if (star < 0) break;
                        if (star + 1 < segment.Length && segment[star + 1] == '*')
                        { end = star + 2; continue; }
                        end = star;
                        break;
                    }
                    if (end >= segment.Length) end = segment.Length;
                    var lbl = new Label(segment.Substring(pick + 1, end - pick - 1));
                    lbl.AddToClassList("chat-message-italic");
                    lbl.style.whiteSpace = WhiteSpace.Normal;
                    container.Add(lbl);
                    i = end + 1;
                }
                else
                {
                    int end = segment.IndexOf('`', pick + 1);
                    if (end < 0) end = segment.Length;
                    var lbl = new Label(segment.Substring(pick + 1, end - pick - 1));
                    lbl.AddToClassList("chat-message-code");
                    lbl.style.whiteSpace = WhiteSpace.Normal;
                    container.Add(lbl);
                    i = end + 1;
                }
            }
        }

        private void BuildMessageContent(VisualElement container, string msg, VisualElement line)
        {
            var matches = ParseLinkMatches(msg);
            int pos = 0;
            var buttonLinks = new List<LinkMatch>();
            foreach (var m in matches)
            {
                if (m.Start > pos)
                    AddMarkdownContentWithPeriodBreaks(container, msg.Substring(pos, m.Start - pos));
                string url = m.Url ?? ResolveLinkUrl(m.Text);
                if (m.IsButton)
                {
                    buttonLinks.Add(new LinkMatch { Text = m.Text, Url = url, IsButton = true });
                }
                else
                {
                    var linkEl = new Label(m.Text);
                    linkEl.AddToClassList("chat-message-text");
                    linkEl.AddToClassList("chat-message-link");
                    linkEl.style.whiteSpace = WhiteSpace.Normal;
                    string linkUrl = url;
                    string term = m.Text;
                    linkEl.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (!string.IsNullOrEmpty(linkUrl) && linkUrl.StartsWith("https://"))
                            Application.OpenURL(linkUrl);
                    });
                    linkEl.RegisterCallback<PointerEnterEvent>(_ => ShowInfoPopupForTerm(term));
                    linkEl.RegisterCallback<PointerLeaveEvent>(_ => HideInfoPopup());
                    linkEl.userData = linkUrl;
                    container.Add(linkEl);
                }
                pos = m.Start + m.Length;
            }
            if (pos < msg.Length)
                AddMarkdownContentWithPeriodBreaks(container, msg.Substring(pos));
            if (buttonLinks.Count > 0)
            {
                var buttonRow = new VisualElement();
                buttonRow.AddToClassList("chat-message-buttons");
                foreach (var b in buttonLinks)
                {
                    string linkUrl = b.Url ?? ResolveLinkUrl(b.Text);
                    var btn = new Button { text = b.Text };
                    btn.AddToClassList("chat-message-link-button");
                    if (!string.IsNullOrEmpty(linkUrl) && linkUrl.StartsWith("https://"))
                    {
                        string u = linkUrl;
                        btn.clicked += () => Application.OpenURL(u);
                    }
                    buttonRow.Add(btn);
                }
                line.Add(buttonRow);
            }
        }

        private void ShowInfoPopupForTerm(string term)
        {
            if (infoPopup == null || infoPopupTitle == null || infoPopupText == null) return;
            string title = term;
            string body = null;
            if (hyperlinkDictionary != null && hyperlinkDictionary.TryGetEntry(term, out var e))
            {
                if (e.type == HyperlinkDictionary.Entry.LinkType.Card)
                    body = hyperlinkDictionary.GetCardText(term);
                else if (e.type == HyperlinkDictionary.Entry.LinkType.Rule)
                    body = hyperlinkDictionary.GetRuleText(term);
                if (string.IsNullOrEmpty(body) && !string.IsNullOrEmpty(e.url))
                    body = e.url;
            }
            if (string.IsNullOrEmpty(body)) body = term;
            infoPopupTitle.text = title;
            infoPopupText.text = body;
            infoPopup.style.display = DisplayStyle.Flex;
        }

        private void HideInfoPopup()
        {
            if (infoPopup != null)
                infoPopup.style.display = DisplayStyle.None;
        }

        private void ShowChatBoxAndLog()
        {
            if (chatBox != null)
            {
                chatBox.style.display = DisplayStyle.Flex;
                unreadCount = 0;
                UpdateBadge();
            }
        }

        public void SendSystemMessage(string system_message)
        {
            SendChat($"__SYSTEM__:{system_message}");
        }

        public void Connect()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
#if UNITY_6000_0_OR_NEWER || UNITY_2021_3_OR_NEWER
            if (connectionMode == ConnectionMode.RemoteWebSocket && !is_connecting && (websocket == null || websocket.State != WebSocketState.Open))
            {
                reconnect_attempts = 0;
                StartCoroutine(ConnectToWebSocket());
            }
#endif
#endif
        }

        public void Disconnect()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
#if UNITY_6000_0_OR_NEWER || UNITY_2021_3_OR_NEWER
            if (connectionMode == ConnectionMode.RemoteWebSocket)
                DisconnectWebSocket();
#endif
#endif
        }

        public static MenuChatUIUXml Get()
        {
            return ui_list.Count > 0 ? ui_list[0] : null;
        }

        /// <summary>Call from a uGUI button (or other script) to open the chat when using disableWhenChatClosed. Sets sort order to 0 and shows the chat box.</summary>
        public void OpenFromExternal()
        {
            if (uiDocument?.rootVisualElement != null && root == null)
                BindUI();
            ShowChatBox();
        }
    }
}
