# MenuChat 1.0

Unity menu chat system for talking to LLM chat provider.

## INSTRUCTIONS

**Install via Package Manager (Git URL):**

1. Package Manager → + → Add package from git URL → `https://github.com/lxpk/menuchat.git?path=/Packages/com.lxpk.menuchat`
2. Import the sample via Package Manager (Samples → MenuChat Sample Scene → Import)

No additional packages are required. WebSocket support is embedded (NativeWebSocket, Apache 2.0).

## UNITY VERSION SUPPORT

- **Unity 2021.3, 2022, 2023:** Full support. WebSocket code is embedded from [NativeWebSocket](https://github.com/endel/NativeWebSocket). Supports WebGL and editor.
- **Unity 6.0 or later:** Full support. `IgnoreCertificateErrors` is available for self-signed certificates on Unity 6+.

## THIRD PARTY

This package embeds [NativeWebSocket](https://github.com/endel/NativeWebSocket) (Endel Dreyer, Jiri Hybek) under Apache 2.0. See `Third Party Notices.md` in the package for details.

## Notes

- **IgnoreCertificateErrors:** On Unity 6+, self-signed certs can be bypassed. On 2021.3, use `wss://` with valid certs or `ws://` for local dev.
- **WebGL:** Supported via embedded WebSocket implementation.
