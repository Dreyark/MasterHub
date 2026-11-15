# Stream Deck plugins in MasterHub – compatibility

MasterHub runs Elgato Stream Deck plugins (`.streamDeckPlugin`) with its own host. This page lists plugins and whether
they work.

## How a plugin is checked

**Automatic check (in the app).** Before MasterHub starts a plugin it checks it
(`Core/Integrations/StreamDeck/PluginCompatibility.cs`). The Action Store shows the result as a badge on the plugin
card ("Runs here" / "May partly work" / "Cannot run here") together with the reasons. The online catalog shows the same
badge for installed plugins. The check covers:

| Reason | Verdict | What to do |
| :-- | :-- | :-- |
| `OtherSystemOnly` – the manifest's `OS` lists no `windows` (only Linux / macOS), or the only program is a shell script (`run.sh`) | cannot run | – (a plugin installed from the store that is made for another system is removed again, with the reason) |
| `NodeMissing` – a Node.js plugin, no Node.js found | cannot run | **Download Node.js…** on the card (asks first; nodejs.org, SHA-256 checked, kept in `%APPDATA%\MasterHub\runtime`) |
| `NodeTooOld` – the Node.js found is older than the manifest's `Nodejs.Version` | cannot run | same button |
| `NoBrowser` – an HTML plugin without Edge or Chrome | cannot run | install Edge or Chrome |
| `ExecutableMissing` – the plugin's program file is gone | cannot run | reinstall the plugin |
| `NeedsNewerStreamDeck` – `Software.MinimumVersion` is above the version MasterHub reports (6.5) | partial | try it; some features may be missing |

A plugin that uses a lot of CPU (more than 25 % of one core on average over about 30 s) is flagged on its card. It is
not stopped.

**Tried in MasterHub (by hand).** Whether the plugin's actions really work on the deck – keys, dials, property
inspector, states – can only be checked by trying it. Fill in the table below after a real test. The automatic columns
can be generated with `PluginCompatibility.ToMarkdown(...)` from the installed plugins.

## Plugins

| Plugin | Version | Runtime | Automatic check | Reasons | Tried in MasterHub |
| :-- | :-- | :-- | :-: | :-- | :-- |
| *(no plugin has been tried and recorded here yet)* | | | | | |
