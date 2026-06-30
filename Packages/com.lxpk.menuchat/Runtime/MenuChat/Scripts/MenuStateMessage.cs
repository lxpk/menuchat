using System;
using System.Text.RegularExpressions;

namespace CardChat.UI
{
    /// <summary>
    /// Plain serializable JSON shapes for the hidden <c>menustate</c> protocol, plus a small
    /// helper that builds and recognises the wire messages. No external dependencies.
    /// </summary>
    /// <remarks>
    /// Outbound (Unity -&gt; server):
    /// <code>
    /// { "hidden_context": { "type": "menustate", "content": "..." }, "chat_mode": "menuchat", "model": "Elowyn" }
    /// </code>
    /// Inbound acknowledgement (server -&gt; Unity, no LLM reply triggered):
    /// <code>
    /// { "object": "hidden_context.queued", "character": "Elowyn", "interaction_id": "...", "wrapped": "..." }
    /// </code>
    /// </remarks>
    [Serializable]
    internal struct HiddenContextPayload
    {
        public string type;     // always "menustate"
        public string content;
    }

    [Serializable]
    internal struct MenuStateWsMessage
    {
        public HiddenContextPayload hidden_context;
        public string chat_mode;           // "menuchat"
        public string model;               // character name, optional
    }

    [Serializable]
    internal struct HiddenContextAck
    {
        public string @object;             // "hidden_context.queued"
        public string character;
        public string interaction_id;
        public string wrapped;
    }

    /// <summary>
    /// Builds the outbound hidden menustate JSON and recognises the inbound queued acknowledgement.
    /// The JSON is assembled by hand (rather than via <see cref="UnityEngine.JsonUtility"/>) so the
    /// nested object and the optional <c>model</c> field serialise exactly as the server expects.
    /// </summary>
    internal static class MenuStateJson
    {
        public const string MenustateType = "menustate";
        public const string DefaultChatMode = "menuchat";
        public const string QueuedAckObject = "hidden_context.queued";

        private static readonly Regex EscapePattern = new Regex("[\\u0000-\\u001F\\\\\"/]");

        /// <summary>
        /// Serialize a hidden menustate message. Returns <c>null</c> when <paramref name="content"/>
        /// is null or empty so callers never send an empty update.
        /// </summary>
        public static string SerializeHiddenMenustate(string content, string chatMode, string model)
        {
            if (string.IsNullOrEmpty(content)) return null;
            string mode = string.IsNullOrEmpty(chatMode) ? DefaultChatMode : chatMode;
            string modelField = string.IsNullOrEmpty(model)
                ? string.Empty
                : ", \"model\": \"" + Escape(model) + "\"";
            return "{\"hidden_context\": {\"type\": \"" + MenustateType + "\", \"content\": \""
                 + Escape(content) + "\"}, \"chat_mode\": \"" + Escape(mode) + "\"" + modelField + "}";
        }

        /// <summary>True when a raw inbound message is the silent <c>hidden_context.queued</c> ack.</summary>
        public static bool IsHiddenContextQueuedAck(string message)
        {
            return !string.IsNullOrEmpty(message)
                && message.IndexOf("\"object\"", StringComparison.Ordinal) >= 0
                && message.IndexOf(QueuedAckObject, StringComparison.Ordinal) >= 0;
        }

        /// <summary>JSON-escape a string (control chars, quotes, backslash, forward slash).</summary>
        public static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return EscapePattern.Replace(text, m =>
            {
                char c = m.Value[0];
                switch (c)
                {
                    case '\\': return "\\\\";
                    case '"': return "\\\"";
                    case '/': return "\\/";
                    case '\n': return "\\n";
                    case '\r': return "\\r";
                    case '\t': return "\\t";
                    default: return "\\u" + ((int)c).ToString("X4");
                }
            });
        }
    }
}
