using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UIE = UnityEngine.UIElements;

namespace CardChat.UI
{
    /// <summary>
    /// Scans active uGUI canvases and UI Toolkit documents for on-screen content and emits a compact,
    /// six-field "menustate" text tree whenever that content changes. <see cref="MenuChatUIUXml"/>
    /// subscribes to <see cref="OnMenuStateChanged"/> and forwards each change to the server as a hidden
    /// <c>menustate</c> update so the LLM knows what menus are visible — without anything appearing in
    /// the chat log.
    /// </summary>
    /// <remarks>
    /// This component is standalone: it has no compile-time dependency on <see cref="MenuChatUIUXml"/>.
    /// The emitted text follows the template:
    /// <code>
    /// MENU USER: {menuUser}
    /// TITLE: {active panel or window title}
    /// PATH: {breadcrumb / screen path}
    /// BUTTONS: {comma-separated visible interactive button labels}
    /// CONTENTS: {key text labels and display values}
    /// ACTIONS: {input field contents / in-flight actions}
    /// </code>
    /// </remarks>
    public class MenuStateScanner : MonoBehaviour
    {
        /// <summary>Maximum characters retained per field before truncation.</summary>
        public const int MaxFieldLength = 512;

        [Header("Scan Targets")]
        [Tooltip("uGUI canvases to scan. If empty, all active root canvases in the scene are scanned.")]
        public List<Canvas> canvasesToScan = new List<Canvas>();

        [Tooltip("UI Toolkit documents to scan. If empty, all active UIDocuments in the scene are scanned.")]
        public List<UIE.UIDocument> uiDocumentsToScan = new List<UIE.UIDocument>();

        [Tooltip("Skip the MenuChat UI itself (objects named 'MenuChat*', UIDocuments with MenuChatUIUXml, or roots with the 'menuchat-ui' class).")]
        public bool excludeMenuChatCanvas = true;

        [Header("Scan Behaviour")]
        [Tooltip("Seconds between automatic scans. Set to 0 to disable the automatic poll (use ForceScan instead).")]
        public float scanIntervalSeconds = 0.5f;

        [Tooltip("When true, automatic scans are skipped unless the connection predicate reports an open connection.")]
        public bool scanOnlyWhenConnected = true;

        [Header("Output")]
        [Tooltip("Sent as the MENU USER field. May be assigned at runtime (e.g. the logged-in player name).")]
        public string menuUser = "";

        /// <summary>
        /// Fires when the scanned state hash changes. Payload is the full menustate text tree.
        /// Also fires on the very first scan (null -&gt; first hash).
        /// </summary>
        public event Action<string> OnMenuStateChanged;

        /// <summary>
        /// Optional predicate used when <see cref="scanOnlyWhenConnected"/> is true. Returns true when the
        /// transport is open. Assigned by <see cref="MenuChatUIUXml"/>; when null, scans are never gated.
        /// </summary>
        public Func<bool> ConnectionStatusProvider { get; set; }

        private string _lastHash;
        private float _timer;

        private void Update()
        {
            if (scanIntervalSeconds <= 0f) return;
            _timer += Time.unscaledDeltaTime;
            if (_timer < scanIntervalSeconds) return;
            _timer = 0f;

            if (scanOnlyWhenConnected && ConnectionStatusProvider != null && !ConnectionStatusProvider())
                return;

            Scan();
        }

        /// <summary>Trigger a single scan immediately, bypassing the poll interval.</summary>
        public void ForceScan()
        {
            _timer = 0f;
            Scan();
        }

        private void Scan()
        {
            string tree = BuildTextTree();
            string hash = HashTree(tree);
            if (hash == _lastHash) return;
            _lastHash = hash;
            OnMenuStateChanged?.Invoke(tree);
        }

        /// <summary>Build the full menustate text tree for the current scene state. No side effects.</summary>
        public string BuildTextTree()
        {
            string title = null;
            string path = null;
            var buttons = new List<string>();
            var contents = new List<string>();
            var actions = new List<string>();
            var buttonLabels = new HashSet<string>(StringComparer.Ordinal);

            ScanCanvases(buttons, contents, actions, buttonLabels, ref title, ref path);
            ScanUiDocuments(buttons, contents, buttonLabels, ref title);

            var sb = new StringBuilder();
            sb.Append("MENU USER: ").Append(menuUser ?? string.Empty).Append('\n');
            sb.Append("TITLE: ").Append(Truncate(title ?? string.Empty)).Append('\n');
            sb.Append("PATH: ").Append(Truncate(path ?? string.Empty)).Append('\n');
            sb.Append("BUTTONS: ").Append(Truncate(JoinDistinct(buttons))).Append('\n');
            sb.Append("CONTENTS: ").Append(Truncate(JoinDistinct(contents))).Append('\n');
            sb.Append("ACTIONS: ").Append(Truncate(JoinDistinct(actions)));
            return sb.ToString();
        }

        // ----------------------------------------------------------------- uGUI

        private void ScanCanvases(List<string> buttons, List<string> contents, List<string> actions,
            HashSet<string> buttonLabels, ref string title, ref string path)
        {
            foreach (var canvas in GetTargetCanvases())
            {
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                var root = canvas.gameObject;
                if (excludeMenuChatCanvas && IsMenuChatObject(root)) continue;

                if (title == null)
                    title = FindTitle(root.transform);

                // BUTTONS — labels of active uGUI buttons.
                foreach (var btn in root.GetComponentsInChildren<Button>(false))
                {
                    if (btn == null || !btn.isActiveAndEnabled) continue;
                    if (excludeMenuChatCanvas && IsMenuChatObject(btn.gameObject)) continue;
                    string label = GetButtonLabel(btn);
                    if (string.IsNullOrEmpty(label)) continue;
                    buttons.Add(label);
                    buttonLabels.Add(label);
                }

                // ACTIONS — input field values.
                foreach (var input in root.GetComponentsInChildren<InputField>(false))
                {
                    if (input == null || !input.isActiveAndEnabled) continue;
                    if (!string.IsNullOrEmpty(input.text)) actions.Add(input.text);
                }
                foreach (var input in root.GetComponentsInChildren<TMP_InputField>(false))
                {
                    if (input == null || !input.isActiveAndEnabled) continue;
                    if (!string.IsNullOrEmpty(input.text)) actions.Add(input.text);
                }
            }

            // PATH — parent chain from canvas root down to the first interactive element.
            path = BuildPathToFirstInteractive();

            // CONTENTS — remaining visible text, excluding titles and button labels.
            foreach (var canvas in GetTargetCanvases())
            {
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                var root = canvas.gameObject;
                if (excludeMenuChatCanvas && IsMenuChatObject(root)) continue;

                foreach (var text in root.GetComponentsInChildren<Text>(false))
                {
                    if (text == null || !text.isActiveAndEnabled) continue;
                    AddContent(text.gameObject, text.text, contents, buttonLabels, title);
                }
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(false))
                {
                    if (text == null || !text.isActiveAndEnabled) continue;
                    AddContent(text.gameObject, text.text, contents, buttonLabels, title);
                }
            }
        }

        private void AddContent(GameObject owner, string value, List<string> contents,
            HashSet<string> buttonLabels, string title)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (excludeMenuChatCanvas && IsMenuChatObject(owner)) return;
            value = value.Trim();
            if (buttonLabels.Contains(value)) return;
            if (title != null && value == title) return;
            contents.Add(value);
        }

        private IEnumerable<Canvas> GetTargetCanvases()
        {
            if (canvasesToScan != null && canvasesToScan.Count > 0)
            {
                foreach (var c in canvasesToScan)
                    if (c != null) yield return c;
                yield break;
            }
#if UNITY_2023_1_OR_NEWER || UNITY_6000_0_OR_NEWER
            var all = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
#else
            var all = UnityEngine.Object.FindObjectsOfType<Canvas>();
#endif
            foreach (var c in all)
                if (c != null && c.isRootCanvas) yield return c;
        }

        private static string GetButtonLabel(Button btn)
        {
            var tmp = btn.GetComponentInChildren<TMP_Text>(false);
            if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text)) return tmp.text.Trim();
            var text = btn.GetComponentInChildren<Text>(false);
            if (text != null && !string.IsNullOrWhiteSpace(text.text)) return text.text.Trim();
            return null;
        }

        private string FindTitle(Transform root)
        {
            // First active Text/TMP_Text whose GameObject is named/tagged Title or Header.
            foreach (var text in root.GetComponentsInChildren<Text>(false))
            {
                if (text == null || !text.isActiveAndEnabled) continue;
                if (LooksLikeTitle(text.gameObject) && !string.IsNullOrWhiteSpace(text.text))
                    return text.text.Trim();
            }
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (text == null || !text.isActiveAndEnabled) continue;
                if (LooksLikeTitle(text.gameObject) && !string.IsNullOrWhiteSpace(text.text))
                    return text.text.Trim();
            }
            // Fallback: the root canvas object name.
            return root.gameObject.name;
        }

        private static bool LooksLikeTitle(GameObject go)
        {
            string n = go.name;
            if (n.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Header", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            try
            {
                if (go.CompareTag("Title") || go.CompareTag("Header")) return true;
            }
            catch (UnityException)
            {
                // Tags not defined in this project — name match is sufficient.
            }
            return false;
        }

        private string BuildPathToFirstInteractive()
        {
            foreach (var canvas in GetTargetCanvases())
            {
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                var root = canvas.gameObject;
                if (excludeMenuChatCanvas && IsMenuChatObject(root)) continue;

                Transform interactive = null;
                var btn = root.GetComponentInChildren<Button>(false);
                if (btn != null && btn.isActiveAndEnabled) interactive = btn.transform;
                if (interactive == null)
                {
                    var input = root.GetComponentInChildren<InputField>(false);
                    if (input != null && input.isActiveAndEnabled) interactive = input.transform;
                }
                if (interactive == null) continue;

                var chain = new List<string>();
                var t = interactive;
                while (t != null)
                {
                    chain.Add(t.gameObject.name);
                    if (t == root.transform) break;
                    t = t.parent;
                }
                chain.Reverse();
                return string.Join("/", chain);
            }
            return null;
        }

        // ----------------------------------------------------------- UI Toolkit

        private void ScanUiDocuments(List<string> buttons, List<string> contents,
            HashSet<string> buttonLabels, ref string title)
        {
            foreach (var doc in GetTargetUiDocuments())
            {
                if (doc == null || !doc.isActiveAndEnabled) continue;
                var root = doc.rootVisualElement;
                if (root == null) continue;
                if (excludeMenuChatCanvas && IsMenuChatDocument(doc, root)) continue;
                CollectFromVisualElement(root, false, buttons, contents, buttonLabels, ref title);
            }
        }

        /// <summary>
        /// Recursively collect buttons / contents / title from a UI Toolkit tree. Exposed internally so
        /// tests can exercise the traversal against a hand-built <see cref="UIE.VisualElement"/> tree.
        /// </summary>
        internal static void CollectFromVisualElement(UIE.VisualElement element, bool insideButton,
            List<string> buttons, List<string> contents, HashSet<string> buttonLabels, ref string title)
        {
            if (element == null) return;

            bool elementIsButton = insideButton;
            if (element is UIE.Button btn)
            {
                elementIsButton = true;
                if (!string.IsNullOrWhiteSpace(btn.text))
                {
                    string label = btn.text.Trim();
                    buttons.Add(label);
                    buttonLabels.Add(label);
                }
            }
            else if (element is UIE.Label label)
            {
                if (!insideButton && !string.IsNullOrWhiteSpace(label.text))
                {
                    string value = label.text.Trim();
                    if (title == null && LooksLikeTitleElement(label))
                        title = value;
                    else if (!buttonLabels.Contains(value) && value != title)
                        contents.Add(value);
                }
            }

            int count = element.childCount;
            for (int i = 0; i < count; i++)
                CollectFromVisualElement(element[i], elementIsButton, buttons, contents, buttonLabels, ref title);
        }

        private static bool LooksLikeTitleElement(UIE.VisualElement el)
        {
            if (el.ClassListContains("title") || el.ClassListContains("header")) return true;
            string n = el.name;
            if (string.IsNullOrEmpty(n)) return false;
            return n.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("header", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private IEnumerable<UIE.UIDocument> GetTargetUiDocuments()
        {
            if (uiDocumentsToScan != null && uiDocumentsToScan.Count > 0)
            {
                foreach (var d in uiDocumentsToScan)
                    if (d != null) yield return d;
                yield break;
            }
#if UNITY_2023_1_OR_NEWER || UNITY_6000_0_OR_NEWER
            var all = UnityEngine.Object.FindObjectsByType<UIE.UIDocument>(FindObjectsSortMode.None);
#else
            var all = UnityEngine.Object.FindObjectsOfType<UIE.UIDocument>();
#endif
            foreach (var d in all)
                if (d != null) yield return d;
        }

        private static bool IsMenuChatDocument(UIE.UIDocument doc, UIE.VisualElement root)
        {
            if (doc.GetComponent<MenuChatUIUXml>() != null) return true;
            if (doc.gameObject.name.StartsWith("MenuChat", StringComparison.Ordinal)) return true;
            if (string.Equals(doc.gameObject.name, "MenuChatUI", StringComparison.Ordinal)) return true;
            if (root != null && root.ClassListContains("menuchat-ui")) return true;
            return false;
        }

        // ----------------------------------------------------------------- util

        private static bool IsMenuChatObject(GameObject go)
        {
            return go != null && go.name.StartsWith("MenuChat", StringComparison.Ordinal);
        }

        private static string JoinDistinct(List<string> values)
        {
            if (values == null || values.Count == 0) return string.Empty;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var v in values)
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                string trimmed = v.Trim();
                if (!seen.Add(trimmed)) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(trimmed);
            }
            return sb.ToString();
        }

        private static string Truncate(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= MaxFieldLength) return value;
            return value.Substring(0, MaxFieldLength);
        }

        private static string HashTree(string tree)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(tree ?? string.Empty));
                return Convert.ToBase64String(bytes, 0, 8);
            }
        }

        /// <summary>Reset change-detection state so the next scan always fires. For tests.</summary>
        internal void ResetLastHash() => _lastHash = null;
    }
}
