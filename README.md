# Unity AI Codex OAuth Bridge

Use Codex / ChatGPT OAuth inside Unity AI Assistant without manually entering an `OPENAI_API_KEY`.

This package patches the local Unity AI Assistant relay metadata so Unity's Codex provider can start the bundled Codex ACP bridge while Codex itself uses the existing Codex CLI ChatGPT login state.

## Status

- Version: `0.1.1`
- Platform: Windows only
- Tested targets: Unity AI Assistant `2.7.0-pre.1` and `2.7.0-pre.3` on Unity 6.4
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
https://github.com/AlperFikret/unity-ai-codex-oauth-bridge.git
```

## Use

Run:

```text
Tools > Unity AI Codex OAuth > Setup Codex OAuth
```

Then open Unity AI Assistant and select `Codex` as the provider.

When Unity asks to approve a `codex-mcp-client` connection from the Unity AI Assistant package cache, approve it if you want Codex to inspect or modify the current Unity project. That permission is required for scene and asset tools.

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

The bridge only patches relay binaries whose Codex provider metadata matches a known safe signature. Unsupported Unity AI Assistant builds are refused until a matching signature is added.

If Codex can answer normally but says `No Unity Editor instances found` or `instance_count: 0` when asked to inspect the scene, it may be using a global Codex Unity MCP server instead of Unity AI Assistant's built-in `unity-mcp-gateway`. Start a new Assistant chat and try:

```text
Do not use the global unityMCP server. Use the Unity AI Assistant provided MCP server named unity-mcp-gateway. List the current Unity scene hierarchy.
```

See [troubleshooting](Documentation~/troubleshooting.md) for common failure cases.
