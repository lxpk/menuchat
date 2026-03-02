#if HAS_LLMUNITY
using System;
using System.Threading.Tasks;
using LLMUnity;
using UnityEngine;

namespace CardChat.UI
{
    /// <summary>
    /// Implements integration with LLMUnity for local chat as an alternative to remote WebSocket.
    /// Isolates LLMUnity dependencies from MenuChatUIUXml via IMenuChatLocalLLM interface.
    /// Assign this component to MenuChatUIUXml's localChatProvider field for LocalLLM connection mode.
    /// Define HAS_LLMUNITY in Scripting Define Symbols to enable this component.
    /// </summary>
    public class MenuChatLocalLLM : MonoBehaviour, IMenuChatLocalLLM
    {
        [Header("Local LLM")]
        [Tooltip("LLMCharacter from LLMUnity. Required for local chat.")]
        public LLMCharacter llmCharacter;

        public bool IsAvailable => llmCharacter != null;

        public async Task<string> ChatAsync(string message, Action<string> onPartial, Action onComplete)
        {
            if (llmCharacter == null)
            {
                onComplete?.Invoke();
                return null;
            }
            try
            {
                Callback<string> partialCb = onPartial != null ? s => onPartial(s) : null;
                EmptyCallback completeCb = onComplete != null ? () => onComplete() : null;
                var result = await llmCharacter.Chat(message, partialCb, completeCb, addToHistory: true);
                return result;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MenuChatLocalLLM] Chat error: {e.Message}");
                onComplete?.Invoke();
                return null;
            }
        }
    }
}
#endif
