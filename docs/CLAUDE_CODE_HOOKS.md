# Claude Code on your deck

MasterHub can show what your Claude Code sessions are doing: working, waiting for you, finished. A key can change colour and text, and the deck's edge light pulses orange when a session needs you.

## What is sent

`masterhub-cli --claude-hook` is called by Claude Code's hooks. It reads the hook's input and forwards **four things only** to the running MasterHub app on this PC:

| Sent | Example |
| :-- | :-- |
| Session id | `abc-123` |
| Project folder **name** (not the path) | `MasterHub` |
| Event | `PermissionRequest` |
| Tool name – only for a permission or notification | `Bash` |

Your prompts, tool inputs, file contents, messages and paths are **never** copied out of the hook input. The command prints nothing (hook output can reach the model), always exits with 0 and gives up after 3 seconds, so it can never block or slow down Claude Code. When MasterHub is not running it does nothing.

## Set up

1. Start MasterHub once (it creates the local API). `masterhub-cli.exe` is in the same folder as `MasterHub.exe`.
2. Add the hooks to `~/.claude/settings.json` (all projects) or `.claude/settings.json` (one project). Use the full path to the exe:

```json
{
  "hooks": {
    "SessionStart":     [{ "hooks": [{ "type": "command", "command": "\"C:\\Program Files\\MasterHub\\masterhub-cli.exe\" --claude-hook" }] }],
    "UserPromptSubmit": [{ "hooks": [{ "type": "command", "command": "\"C:\\Program Files\\MasterHub\\masterhub-cli.exe\" --claude-hook" }] }],
    "Notification":     [{ "hooks": [{ "type": "command", "command": "\"C:\\Program Files\\MasterHub\\masterhub-cli.exe\" --claude-hook" }] }],
    "PostToolUse":      [{ "hooks": [{ "type": "command", "command": "\"C:\\Program Files\\MasterHub\\masterhub-cli.exe\" --claude-hook" }] }],
    "Stop":             [{ "hooks": [{ "type": "command", "command": "\"C:\\Program Files\\MasterHub\\masterhub-cli.exe\" --claude-hook" }] }],
    "SessionEnd":       [{ "hooks": [{ "type": "command", "command": "\"C:\\Program Files\\MasterHub\\masterhub-cli.exe\" --claude-hook" }] }]
  }
}
```

`PostToolUse` moves a session from "waiting" back to "working" after you approved something. It runs after every tool call, which is the most frequent hook; leave it out if you only want to see "waiting" and "finished".

## What the states mean

| Claude Code event | Shown as |
| :-- | :-- |
| `SessionStart` | idle (grey) |
| `UserPromptSubmit`, `PreToolUse`, `PostToolUse` | working (blue) |
| `Notification`, `PermissionRequest` | **waiting** (orange) – it needs you |
| `Stop` | done (green) – it finished its turn |
| `SessionEnd` | removed |

A session that sends nothing for 6 hours is dropped (a closed terminal sends no `SessionEnd`).

## Show it on a key

In the key's **Looks** ("When" rules) pick *An app waits for you*, *An app is working*, *An app is done* or *An app reports an error* and set a colour or a label. Labels understand `{status}` (the most urgent state), `{status_name}` (its project), `{waiting}` and `{working}` (counts). For example a rule *An app waits for you* with the title `{status_name}` turns the key orange and names the project that asks.

For the edge light turn on **Status lights** of the base station; a waiting session then pulses the edge orange (orange always means "needs your attention").

## Other apps

Any program can report the same states without a server of its own:

```
POST http://127.0.0.1:<port>/api/state      (Authorization: Bearer <token>, Content-Type: application/json)
{ "source": "ci", "items": [ { "id": "build", "state": "waiting", "label": "Build 12", "text": "needs approval" } ] }
```

States: `waiting`, `working`, `done`, `error`, `idle`, `offline`. Up to 50 items per request; one bad item refuses the whole request. `GET /api/status` lists everything. The port and token are in `%APPDATA%\MasterHub` (`api-port`, `api-token`; the token is DPAPI-protected). The source name `claude` is reserved for Claude Code.
