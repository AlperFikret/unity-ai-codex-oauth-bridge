# Troubleshooting

## `Missing credentials: OPENAI_API_KEY`

Run:

```text
Tools > Unity AI Codex OAuth > Diagnostics
```

If the relay is unpatched, run:

```text
Tools > Unity AI Codex OAuth > Setup Codex OAuth
```

## Codex CLI Not Found

Install or open Codex, then run setup again. The bridge searches:

- `PATH`
- `%LOCALAPPDATA%\Microsoft\WindowsApps\codex.exe`
- `C:\Program Files\WindowsApps\OpenAI.Codex_*\app\resources\codex.exe`

## Codex Not Logged In

Setup opens `codex login` in PowerShell. Complete the browser sign-in and return to Unity.

## `No Unity Editor instances found` or `instance_count: 0`

This usually means OAuth is working, but Codex picked a different Unity MCP server than the Unity AI Assistant session expects.

Unity AI Assistant provides its own MCP server named `unity-mcp-gateway`. If your global Codex config also has a Unity MCP server such as `unityMCP` or `mcp-for-unity`, Codex may choose that global server and fail to see the current Unity Editor.

Start a new Assistant chat and try:

```text
Do not use the global unityMCP server. Use the Unity AI Assistant provided MCP server named unity-mcp-gateway. List the current Unity scene hierarchy.
```

If Unity shows a `New MCP Connection` dialog for `codex-mcp-client` from the Unity AI Assistant package cache, approve it only if you trust the current project and want Codex to inspect or modify scenes, assets, and scripts.

## Unsupported Relay Binary

The bridge refuses to patch unknown relay binaries. This is intentional. Unity may have changed the bundled relay code, and the patch signature must be updated before it is safe to modify.

Version `0.1.1` includes signatures for Unity AI Assistant `2.7.0-pre.1` and `2.7.0-pre.3`.

## Restore Original Relay

Use:

```text
Tools > Unity AI Codex OAuth > Restore Original Unity Relay
```

The bridge restores `relay_win.exe` from `relay_win.exe.codex-oauth-backup`.
