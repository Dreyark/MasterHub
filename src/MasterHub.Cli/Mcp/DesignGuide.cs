namespace MasterHub.Cli.Mcp;

/// <summary>The text of <c>masterhub://design-guide</c>: what an assistant needs to know before it designs keys. Written from this project's own hardware notes.</summary>
internal static class DesignGuide
{
    public const string Text = """
        # Designing a MasterHub deck

        ## The hardware
        - **Keys**: 15 LCD keys in a grid of 3 rows × 5 columns. `keys.0` is top left, `keys.14` bottom right. Each key is a 72×72 pixel picture, so a label must be short: **12 characters or fewer**, one or two words. A longer label may be cut off.
        - **Faders, knobs, dial, rollers** exist on other modules; the API edits keys only for now.
        - **The roller module has no lights.** Never plan colour for it.
        - Lights: the base's edge light and the faders/knobs can be set with `set_lights` (profile, off, color, rainbow). It is not saved in a profile and stays until another profile is applied.

        ## Making a key
        - One key = an action (`list_actions`) + how it looks (`title`, `subtitle`, `background`, `text_color` as `#rrggbb`).
        - A change without `action` only changes the look and keeps what the key does.
        - `keys.N:hold` and `keys.N:double` are extra bindings of the same key (press and hold, double press). They take an action and parameter only; the look belongs to the key.
        - Keep contrast: a light background needs dark text. Prefer a few calm colours and one accent over every key in a different colour.
        - **Orange means "needs your attention", red means "live or muted", green means "ready".** Do not use them for decoration.

        ## What you may not do
        These rules are enforced by the app, not by this text. A refused change tells you why:
        - No action that calls an arbitrary service (`HomeAssistantCallService`): use `HomeAssistantToggle`, `HomeAssistantTurnOn` or `HomeAssistantTurnOff` with an entity id.
        - No scripts, installers or shells (`.bat`, `.ps1`, `.vbs`, `cmd`, `powershell` …) as the target of `OpenApplication` or `OpenFile`.
        - Anything that closes, quits, deletes or shuts down (`SystemShutdown`, `SystemRestart`, `SystemSleep`, `CloseApplication`, shortcuts such as Alt+F4 or Ctrl+W) is accepted **only on a hold**: use the control id `keys.N:hold`.

        ## Working method
        1. `list_profiles`, then `get_profile` to see what is there. Do not replace what the user built.
        2. Change one profile at a time. Every write is all-or-nothing and the app takes a backup first.
        3. Use `activate_profile` only when the user asks to see the result on the deck.
        """;
}
