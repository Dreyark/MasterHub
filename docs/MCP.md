# MasterHub for AI assistants (MCP)

`masterhub-cli.exe --mcp` is a [Model Context Protocol](https://modelcontextprotocol.io) server over stdio. It lets an assistant such as Claude design your deck: read profiles, change keys, set the lights. It needs the MasterHub app to be running; it only talks to the app's local API and never touches the deck or the profile files itself.

## Connect

Claude Code:

```
claude mcp add masterhub -- "C:\Program Files\MasterHub\masterhub-cli.exe" --mcp
```

Claude Desktop (`claude_desktop_config.json`; keep a copy of the file first):

```json
{
  "mcpServers": {
    "masterhub": { "command": "C:\\Program Files\\MasterHub\\masterhub-cli.exe", "args": ["--mcp"] }
  }
}
```

`masterhub-cli.exe` sits next to `MasterHub.exe`. The server reads the port and token of the running app again for **every call**, so restarting the app does not break the connection.

## Tools

| Tool | What it does |
| :-- | :-- |
| `design_guide` | The hardware facts and rules (also the resource `masterhub://design-guide`). Read first. |
| `list_modules` | Which modules are connected. |
| `list_actions` | The actions a key can run, with the limits an assistant has for each (`search` narrows it). |
| `list_profiles`, `get_profile` | See what exists. Credentials are never included. |
| `activate_profile` | Put a profile on the deck. |
| `set_control`, `clear_control` | Change or empty a key (`keys.0` … `keys.14`, optional `:hold` / `:double`, optional `page`). All or nothing; the app takes a backup first. |
| `set_lights` | `profile`, `off`, `color` or `rainbow`, until another profile is applied. |

There is **no tool that reaches the hardware directly**.

## Limits (enforced by the app, not by a prompt)

Every request of the MCP server is marked as coming from an assistant, and the app applies these rules to it. A refusal says why and what would be accepted, so the model can retry:

- no `HomeAssistantCallService` (use `HomeAssistantToggle`, `HomeAssistantTurnOn`, `HomeAssistantTurnOff` with an entity id);
- no scripts, installers or shells (`.bat`, `.ps1`, `.vbs`, `.msi`, `cmd`, `powershell` …) as the target of `OpenApplication` / `OpenFile`;
- anything that closes, quits, deletes or shuts down (`SystemShutdown`, `SystemRestart`, `SystemSleep`, `CloseApplication`, shortcuts like Alt+F4 or Ctrl+W) only on a **hold** (`keys.N:hold`), never on a plain press.

The first two can be lifted by **you**, by starting the server with the environment variable `MASTERHUB_MCP_MORE_POWER=1` ("more power"). The hold rule is never lifted. The model cannot set the variable.

The same command-line client (`masterhub-cli key …`) and the plain local API are **not** limited this way: they are your own scripts.

## Not done yet

- `--mcp-deck` (tools that talk to the physical deck: `notify`, `ask_physical`, `read_controls`), because they need the dial layer (T-19).
- Tools for pictures and animations, pages (`add_page`, `delete_page`), `preview_control`, `list_apps`, `list_games`, `create_profile`.
- A "Connect to Claude" button in Settings that edits the config for you (with a backup).
