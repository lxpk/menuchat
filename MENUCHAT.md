# MENUCHAT — MenuState GUI Scanning Implementation Plan

## Goal

Implement `MenuStateScanner`, a Unity component that scans active GUI canvases and UI Toolkit documents for content changes and feeds those changes to `MenuChatUIUXml` as hidden `menustate` updates sent over the WebSocket in menuchat mode.

The server receives these as `hidden_context` messages (type `menustate`), queues them, and injects the accumulated state into the agent's context on the next chat turn — giving the LLM situational awareness of what menus are on screen without exposing anything in the visible chat log.

---

## Protocol Reference

All hidden menustate updates are sent to the server via the existing WebSocket connection at `/ws/v1/chat/completions`.

### Outbound — hidden menustate message
```json
{
  "hidden_context": {
    "type": "menustate",
    "content": "MENU USER: player1\nTITLE: Main Menu\nPATH: /main\nBUTTONS: Play, Quit\nCONTENTS: Welcome to CardCore\nACTIONS: "
  },
  "chat_mode": "menuchat",
  "model": "Elowyn"
}
```

### Inbound — server acknowledgement (no LLM reply triggered)
```json
{
  "object": "hidden_context.queued",
  "character": "Elowyn",
  "interaction_id": "...",
  "wrapped": "<hidden type=menustate>\n...\n</menustate>"
}
```

### Menustate content template
```
MENU USER: {username}
TITLE: {active panel or window title}
PATH: {breadcrumb or screen path}
BUTTONS: {comma-separated visible interactive button labels}
CONTENTS: {key text labels and display values visible in the menu}
ACTIONS: {any pending or in-flight actions}
```

---

## Files to Create

### 1. `Runtime/MenuChat/Scripts/MenuStateScanner.cs`

A `MonoBehaviour` that scans the Unity GUI and emits changes.

**Namespace:** `CardChat.UI`

**Public API:**
```csharp
public class MenuStateScanner : MonoBehaviour
{
    // Serialized settings
    [Header("Scan Targets")]
    public List<Canvas> canvasesToScan;          // uGUI canvases; if empty, scan all active root canvases
    public List<UIDocument> uiDocumentsToScan;   // UI Toolkit documents; if empty, scan all in scene
    public bool excludeMenuChatCanvas = true;    // skip the MenuChat UI itself

    [Header("Scan Behaviour")]
    public float scanIntervalSeconds = 0.5f;     // poll interval
    public bool scanOnlyWhenConnected = true;    // skip scans if WebSocket is not open

    [Header("Output")]
    public string menuUser = "";                 // sent as MENU USER field; can be set at runtime

    // Events
    public event Action<string> OnMenuStateChanged;  // fires when state hash changes; payload = tree text

    // Methods
    public string BuildTextTree();               // build full menustate text now (no side effects)
    public void ForceScan();                     // trigger one scan immediately (bypasses interval)
}
```

**Scan algorithm (uGUI):**
1. Collect target canvases (filter by `canvasesToScan` or find all active root canvases).
2. Optionally exclude the canvas containing the `UIDocument` with `MenuChatUIUXml`.
3. For each canvas, depth-first traverse active `GameObject` children.
4. Extract:
   - **TITLE**: first active `Text`/`TMP_Text` whose name or tag is "Title" or "Header"; fallback to root canvas GameObject name.
   - **PATH**: collect all parent GameObject names from root canvas to first interactive element; join with `/`.
   - **BUTTONS**: collect `.text` from all active `Button` children (uGUI) and `TMPro.TMP_Text` children of buttons.
   - **CONTENTS**: collect all non-empty `Text`/`TMP_Text` values (excluding button labels, title, and any object whose name starts with `"MenuChat"`).
   - **ACTIONS**: collect `InputField.text` and `TMP_InputField.text` values from active input fields.
5. Deduplicate and truncate each field to 512 chars maximum.

**Scan algorithm (UI Toolkit):**
1. Collect target `UIDocument` instances (filter or find all active).
2. Optionally exclude any `UIDocument` whose root contains the class `menuchat-ui` or whose `name` is `"MenuChatUI"`.
3. Recursively traverse `VisualElement` tree:
   - **BUTTONS**: collect `.text` from `Button` elements.
   - **CONTENTS**: collect `.text` from `Label` elements not inside a button.
   - **TITLE**: first `Label` with USS class `"title"`, `"header"`, or name containing "title"/"header".
4. Merge results with uGUI scan output.

**Change detection:**
```csharp
private string _lastHash = null;

private static string HashTree(string tree)
{
    using var sha = System.Security.Cryptography.SHA256.Create();
    var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(tree));
    return System.Convert.ToBase64String(bytes, 0, 8); // 8-byte prefix is sufficient
}
```

Fire `OnMenuStateChanged` only when `HashTree(newTree) != _lastHash`. Update `_lastHash` on fire.

---

### 2. `Runtime/MenuChat/Scripts/MenuStateMessage.cs`

Plain C# structs for JSON serialization — no external dependencies.

```csharp
namespace CardChat.UI
{
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
}
```

Use `JsonUtility.ToJson` for serialization. Note `JsonUtility` does not support nested classes with `[Serializable]` attribute via `JsonUtility.ToJson` for top-level calls — use `JsonUtility.ToJson(new MenuStateWsMessage { ... })` which works for plain structs/classes with `[Serializable]`.

> **Alternative**: serialize the JSON string manually using `string.Format` / `StringBuilder` to avoid JsonUtility limitations with nested types. This is acceptable given the fixed schema.

---

### 3. Changes to `Runtime/MenuChat/Scripts/MenuChatUIUXml.cs`

Add the following to the existing class:

**New serialized fields (under a new `[Header("MenuState Scanning")]`):**
```csharp
[Header("MenuState Scanning")]
[Tooltip("Assign a MenuStateScanner component to auto-send hidden menustate updates.")]
[SerializeField] private MenuStateScanner menuStateScanner;
public bool autoSendMenuState = true;
public string menustateChatMode = "menuchat";
public string characterName = "";   // optional; sent as "model" field in menustate messages
```

**New internal method — `SendHiddenMenuState`:**
```csharp
private void SendHiddenMenuStateWebSocket(string content)
{
    if (websocket == null || websocket.State != WebSocketState.Open) return;
    // Build JSON manually to avoid JsonUtility nested-type limitation
    string escaped = EscapeJsonString(content);
    string modelField = string.IsNullOrEmpty(characterName) ? ""
        : $", \"model\": \"{EscapeJsonString(characterName)}\"";
    string json = $"{{\"hidden_context\": {{\"type\": \"menustate\", \"content\": \"{escaped}\"}}, "
                + $"\"chat_mode\": \"{menustateChatMode}\"{modelField}}}";
    try { websocket.SendText(json); }
    catch (Exception e) { Debug.LogError($"[MenuChatUIUXml] MenuState send error: {e.Message}"); }
}
```

**Wire up scanner in `Start()`:**
```csharp
if (menuStateScanner != null && autoSendMenuState)
    menuStateScanner.OnMenuStateChanged += OnMenuStateChangedHandler;
```

**Handler:**
```csharp
private void OnMenuStateChangedHandler(string treeText)
{
    if (connectionMode != ConnectionMode.RemoteWebSocket) return;
#if !UNITY_WEBGL || UNITY_EDITOR
#if UNITY_6000_0_OR_NEWER || UNITY_2021_3_OR_NEWER
    if (websocket != null && websocket.State == WebSocketState.Open)
        SendHiddenMenuStateWebSocket(treeText);
#endif
#endif
}
```

**Unsubscribe in `OnDestroy()`:**
```csharp
if (menuStateScanner != null)
    menuStateScanner.OnMenuStateChanged -= OnMenuStateChangedHandler;
```

**Updated `ProcessWebSocketMessage` — handle hidden_context.queued ack:**
```csharp
// At the top of ProcessWebSocketMessage, before the existing content match:
if (message.Contains("\"object\"") && message.Contains("hidden_context.queued"))
{
    // Silently acknowledge; no visible message added to chat log
    return;
}
```

**Public escape hatch for tests:**
```csharp
// internal so tests in the same assembly or friends assembly can call it
internal void SendHiddenMenuStateForTest(string content) => SendHiddenMenuStateWebSocket(content);
```

---

### 4. New file — `Runtime/AssemblyInfo.cs`

Required to give the test assemblies access to `internal` members via `InternalsVisibleTo`. Without this, `SendHiddenMenuStateForTest` and any other `internal` test hooks will not compile in the test projects.

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("com.lxpk.menuchat.tests.editor")]
[assembly: InternalsVisibleTo("com.lxpk.menuchat.tests.runtime")]
```

> Note: Unity asmdef-based assemblies do not use strong-name signing, so no public key is required.

---

## Tests Directory Structure

```
Packages/com.lxpk.menuchat/
├── Tests/
│   ├── Editor/
│   │   ├── com.lxpk.menuchat.tests.editor.asmdef
│   │   ├── MenuStateScannerTests.cs
│   │   ├── MenuStateDiffTests.cs
│   │   └── MenuStateJsonTests.cs
│   └── Runtime/
│       ├── com.lxpk.menuchat.tests.runtime.asmdef
│       └── MenuChatMenuStateIntegrationTests.cs
```

---

## `Tests/Editor/com.lxpk.menuchat.tests.editor.asmdef`

```json
{
  "name": "com.lxpk.menuchat.tests.editor",
  "rootNamespace": "CardChat.UI.Tests.Editor",
  "references": [
    "com.lxpk.menuchat",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
  ],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": [
    "nunit.framework.dll"
  ],
  "autoReferenced": false,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

---

## `Tests/Runtime/com.lxpk.menuchat.tests.runtime.asmdef`

```json
{
  "name": "com.lxpk.menuchat.tests.runtime",
  "rootNamespace": "CardChat.UI.Tests.Runtime",
  "references": [
    "com.lxpk.menuchat",
    "UnityEngine.TestRunner"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": [
    "nunit.framework.dll"
  ],
  "autoReferenced": false,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

---

## `Tests/Editor/MenuStateScannerTests.cs`

**Test class:** `MenuStateScannerTests` (EditMode, NUnit)

These tests exercise `MenuStateScanner.BuildTextTree()` by constructing minimal mock `Canvas`/`GameObject` hierarchies using Unity's `new GameObject()` API (valid in EditMode).

```
[TestFixture]
class MenuStateScannerTests
```

**Test cases:**

| Test | Setup | Assert |
|---|---|---|
| `BuildTextTree_EmptyScene_ReturnsEmptyOrDefault` | No canvases assigned | Returns non-null string; does not throw |
| `BuildTextTree_SingleCanvas_IncludesCanvasName` | New GameObject + Canvas with child Text "Welcome" | Output contains "Welcome" |
| `BuildTextTree_ButtonsAreCollected` | Canvas with 3 Button children with .text set | BUTTONS: field lists all 3 button texts |
| `BuildTextTree_InactiveObjectsExcluded` | Canvas with 1 active Button and 1 inactive Button | Only 1 button in output |
| `BuildTextTree_TitleTextFromTaggedObject` | GameObject named "Title" with Text "Main Menu" | TITLE: field contains "Main Menu" |
| `BuildTextTree_ContentsTruncatedAt512` | Label with 600 char string | CONTENTS: value truncated to ≤ 512 chars |
| `BuildTextTree_MenuChatCanvasExcluded` | excludeMenuChatCanvas=true; canvas named "MenuChatUI" | Not present in output |
| `BuildTextTree_UIDocument_ButtonsCollected` | UIDocument with Button visual element text "Play" | BUTTONS: contains "Play" |

Each test creates a fresh `GameObject`, adds the scanner, sets `canvasesToScan` / `uiDocumentsToScan` directly, calls `BuildTextTree()`, asserts, and destroys the object in `[TearDown]`.

---

## `Tests/Editor/MenuStateDiffTests.cs`

**Test class:** `MenuStateDiffTests` (EditMode, NUnit)

Tests the change-detection logic. Because `_lastHash` is private, expose a `ResetLastHash()` internal method for testing, or use reflection, or test through `ForceScan()` with an event capture.

| Test | Assert |
|---|---|
| `OnMenuStateChanged_FiredOnFirstScan` | Event fires on initial scan (null → first hash) |
| `OnMenuStateChanged_NotFiredIfUnchanged` | Scan twice with same canvas state → event fires exactly once |
| `OnMenuStateChanged_FiredWhenButtonAdded` | Add a button to canvas between scans → event fires second time |
| `OnMenuStateChanged_EventPayloadMatchesBuildTextTree` | Event string equals `BuildTextTree()` return at that moment |
| `ForceScan_IgnoresInterval` | Set `scanIntervalSeconds = 999`, call `ForceScan()` → event fires |

---

## `Tests/Editor/MenuStateJsonTests.cs`

**Test class:** `MenuStateJsonTests` (EditMode, NUnit)

Tests that the JSON serialized for the hidden menustate message is correct and parseable. Test the internal JSON builder method via `InternalsVisibleTo` or a thin wrapper.

| Test | Assert |
|---|---|
| `SerializeHiddenMenustate_ContainsHiddenContextKey` | Output JSON string contains `"hidden_context"` |
| `SerializeHiddenMenustate_TypeIsMenustate` | JSON contains `"type": "menustate"` |
| `SerializeHiddenMenustate_ContentIsEscaped` | Content with `\n` and `"` is properly escaped |
| `SerializeHiddenMenustate_ChatModeIsMenuchat` | JSON contains `"chat_mode": "menuchat"` |
| `SerializeHiddenMenustate_EmptyContentRejected` | Null/empty content → method returns null or throws `ArgumentException` |
| `SerializeHiddenMenustate_RoundTrip` | Parse output with `JsonUtility.FromJson` or `SimpleJSON`, verify fields |

---

## `Tests/Runtime/MenuChatMenuStateIntegrationTests.cs`

**Test class:** `MenuChatMenuStateIntegrationTests` (PlayMode, NUnit)

PlayMode tests run in a full Unity runtime frame loop. These tests:
1. Spin up a minimal mock WebSocket server using `System.Net.WebSockets.HttpListener`/`TcpListener` on localhost.
2. Create a `MenuChatUIUXml` + `MenuStateScanner` game object in a test scene.
3. Configure `MenuChatUIUXml` to connect to the mock server.
4. Verify messages are sent and acks are received.

**Mock server setup helper:**
```csharp
class MockWsServer : IDisposable
{
    public List<string> ReceivedMessages { get; }
    public void EnqueueResponse(string json);
    public void Start(int port);
    public void Dispose();
}
```

| Test | Steps | Assert |
|---|---|---|
| `WhenMenuStateChanges_HiddenMessageIsSentOverWebSocket` | Connect → trigger scanner state change → wait 1 frame | MockServer.ReceivedMessages contains JSON with `"hidden_context"` |
| `HiddenContextQueuedAck_IsNotAddedToChatLog` | Connect → server sends `hidden_context.queued` ack → wait | ChatLogContent has no new entries |
| `HiddenMenustate_NotSentWhenDisconnected` | autoSendMenuState=true but websocket closed → trigger change | No message sent (MockServer.ReceivedMessages empty) |
| `HiddenMenustate_NotSentWhenAutoSendDisabled` | autoSendMenuState=false → trigger change | No hidden message sent |
| `MenustateAndUserMessage_SentInOrder` | Force menustate → user sends "Hello" → inspect messages | ReceivedMessages[0] has hidden_context, [1] has message |

---

## Integration Scene — `Samples/MenuChatSampleScene.unity`

The existing sample scene is the target for manual validation. Extend it with:

**New script: `Samples/IntegrationExampleScripts/MenuStateDemoController.cs`**

A `MonoBehaviour` that cycles the demo UI through distinct states so the scanner output can be observed in the console and verified against what the server receives.

```
class MenuStateDemoController : MonoBehaviour
```

**Scene layout:**

| GameObject | Components | Purpose |
|---|---|---|
| `DemoCanvas` (Canvas) | Canvas, CanvasScaler | uGUI scan target |
| `DemoCanvas/MainMenuPanel` | GameObject | Active by default |
| `DemoCanvas/MainMenuPanel/TitleText` | TMP_Text, name="Title" | TITLE field |
| `DemoCanvas/MainMenuPanel/PlayButton` | Button + TMP_Text "Play" | BUTTONS field |
| `DemoCanvas/MainMenuPanel/SettingsButton` | Button + TMP_Text "Settings" | BUTTONS field |
| `DemoCanvas/MainMenuPanel/BodyText` | TMP_Text "Welcome to CardCore" | CONTENTS field |
| `DemoCanvas/SettingsPanel` | GameObject, inactive by default | Swapped in on state 2 |
| `DemoCanvas/SettingsPanel/TitleText` | TMP_Text, name="Title" "Settings" | TITLE field |
| `DemoCanvas/SettingsPanel/BackButton` | Button + TMP_Text "Back" | BUTTONS field |
| `DemoCanvas/SettingsPanel/VolumeInput` | TMP_InputField | ACTIONS field |
| `UIDocumentDemo` | UIDocument (inline UXML) | UI Toolkit scan target |
| `MenuChatPanel` | ChatPanelUIUXML prefab + MenuStateScanner | System under test |

**`MenuStateDemoController` behaviour:**
- Press **Space** (or every 3 seconds in auto-mode): toggle between MainMenuPanel and SettingsPanel active states.
- On each state change: call `scanner.ForceScan()` and log `BuildTextTree()` to the console.
- Serialized field `public MenuStateScanner scanner` wired in Inspector.

This scene exercises both uGUI and UI Toolkit scan paths against real rendered objects — something EditMode tests with synthetic GameObjects cannot cover.

**Server-side note:** HERMES does not yet implement the `hidden_context` / `hidden_context.queued` protocol. Manual end-to-end testing (step 6) is blocked until that server work is done and is tracked separately. Until then, validate the full round-trip using the PlayMode mock WebSocket server only.

---

## Implementation Order

1. Create `Runtime/AssemblyInfo.cs` — `InternalsVisibleTo` declarations.
2. Create `MenuStateMessage.cs` — JSON types only, no dependencies.
3. Create `MenuStateScanner.cs` — standalone, no MenuChat dependency.
4. Write and pass EditMode tests (`MenuStateScannerTests`, `MenuStateDiffTests`, `MenuStateJsonTests`).
5. Extend `MenuChatUIUXml.cs` — add `characterName` field, scanner hookup, and `SendHiddenMenuStateWebSocket`.
6. Build integration scene — extend `MenuChatSampleScene.unity` with demo canvas layout and `MenuStateDemoController.cs`.
7. Write and pass PlayMode integration tests (`MenuChatMenuStateIntegrationTests`).
8. Test manually against HERMES once server-side `hidden_context` handling is implemented.

---

## Definition of Done

- [ ] `MenuStateScanner.BuildTextTree()` produces the 6-field template format for any active Canvas/UIDocument hierarchy.
- [ ] Change detection fires `OnMenuStateChanged` exactly once per distinct state; never fires for duplicate states.
- [ ] `MenuChatUIUXml` sends a correctly-formed hidden_context JSON when scanner fires and WebSocket is open.
- [ ] Server's `hidden_context.queued` ack is silently swallowed — not shown in the chat log.
- [ ] All EditMode tests pass in Unity Test Runner (headless CI-friendly).
- [ ] All PlayMode tests pass with mock WebSocket server.
- [ ] `MenuChatSampleScene` demo scene cycles between UI states and logs correct `BuildTextTree()` output to console.
- [ ] No changes to the visible chat message flow — hidden messages are invisible to the player.
- [ ] (Deferred) Manual end-to-end test against HERMES once server-side `hidden_context` handling is complete.
