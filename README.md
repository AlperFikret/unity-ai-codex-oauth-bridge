# Unity AI Codex OAuth Bridge

Use Codex / ChatGPT OAuth inside Unity AI Assistant without manually entering an `OPENAI_API_KEY`.

This package patches the local Unity AI Assistant relay metadata so Unity's Codex provider can start the bundled Codex ACP bridge while Codex itself uses the existing Codex CLI ChatGPT login state.

## Status

- Version: `0.1.0`
- Platform: Windows only
- Tested target: Unity AI Assistant `2.7.0-pre.1` on Unity 6.4
- License: MIT

This is an unofficial bridge. It is not affiliated with Unity or OpenAI.

## What It Does

- Finds the installed `com.unity.ai.assistant` package.
- Backs up `RelayApp~/relay_win.exe` to `relay_win.exe.codex-oauth-backup`.
- Patches only the local relay provider metadata for Codex.
- Removes Unity Gateway's `OPENAI_API_KEY` requirement for Codex.
- Starts `codex login` if the Codex CLI is not already logged in.
- Enables Codex in Unity AI Assistant and restarts Unity AI Relay.

It does not ship Unity binaries, Codex binaries, API keys, OAuth tokens, or credentials.

## Install

1. Open Unity Package Manager.
2. Choose `Add package from git URL...`.
3. Enter:

```text
https://github.com/<your-user>/unity-ai-codex-oauth-bridge.git
```

## Use

Run:

```text
Tools > Unity AI Codex OAuth > Setup Codex OAuth
```

Then open Unity AI Assistant and select `Codex` as the provider.

Useful commands:

- `Tools > Unity AI Codex OAuth > Diagnostics`
- `Tools > Unity AI Codex OAuth > Restore Original Unity Relay`
- `Tools > Unity AI Codex OAuth > Advanced > Restart Unity AI Relay`

## Restore

If you want to undo the local relay patch:

```text
Tools > Unity AI Codex OAuth > Restore Original Unity Relay
```

This restores the backed-up relay executable. It does not delete your Codex CLI login state.

## Notes

Unity AI Assistant package updates can replace the relay executable. If the `OPENAI_API_KEY` error returns after an update, run setup again.

See [troubleshooting](Documentation~/troubleshooting.md) for common failure cases.
