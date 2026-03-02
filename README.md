# MenuChat 1.0

Unity menu chat system for talking to LLM chat provider.

## INSTRUCTIONS

**Install via Package Manager (Git URL):**
1. Window → Package Manager → + → Add package from git URL
2. Enter: `https://github.com/lxpk/menuchat.git#path:Packages/com.lxpk.menuchat`
3. Import the sample via Package Manager (Samples → MenuChat Sample Scene → Import)

## UNITY VERSION SUPPORT

WebSocket code is conditionally compiled via `UNITY_6000_0_OR_NEWER` and `UNITY_2021_3_OR_NEWER`. Both code paths use the NativeWebSocket API.

### 2021.3 (and 2022, 2023)
Unity 2021.3 lacks NativeWebSocket as a built-in library. The project uses the `com.endel.nativewebsocket` package (added via Git URL). UnityTransport 1.5 is a project dependency but does not provide general WebSocket client capability (only UTP protocol). Supports WebGL usecases and editor use.

### Unity 6.0 or later
Unity 6.0 or later supports NativeWebSocket as a standard Unity library. The same `com.endel.nativewebsocket` package is included for compatibility. Supports WebGL usecases and editor use.

## Differences
- **IgnoreCertificateErrors**: The Unity 6 built-in NativeWebSocket exposes `IgnoreCertificateErrors`; the `com.endel.nativewebsocket` package (2021.3) does not. This is handled via `#if UNITY_6000_0_OR_NEWER` so the assignment is only compiled for Unity 6+. On 2021.3, certificate validation uses platform defaults (self-signed certs may fail; use `wss://` with valid certs or `ws://` for local dev).

