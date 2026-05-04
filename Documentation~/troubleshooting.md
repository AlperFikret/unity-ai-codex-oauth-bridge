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

## Unsupported Relay Binary

The bridge refuses to patch unknown relay binaries. This is intentional. Unity may have changed the bundled relay code, and the patch signature must be updated before it is safe to modify.

## Restore Original Relay

Use:

```text
Tools > Unity AI Codex OAuth > Restore Original Unity Relay
```

The bridge restores `relay_win.exe` from `relay_win.exe.codex-oauth-backup`.
