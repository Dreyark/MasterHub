using System;
using System.Collections.Generic;
using System.Linq;

namespace MasterHub.Core.Profiles.Auto;

/// <summary>
/// Settings of the "assistant" (T-37): profiles that prepare themselves for the programs on this PC, and small suggestions.
/// Everything here is <b>off until the user says yes</b>, and one switch (<see cref="Enabled"/>) turns all of it off again.
/// </summary>
public sealed class AssistSettings
{
    /// <summary>The offer was shown and answered (yes or no), so it is never shown again by itself. Settings can change the answer.</summary>
    public bool Asked { get; set; }

    /// <summary>The master switch: prepared profiles, counting of the time a program is in front, and suggestions.</summary>
    public bool Enabled { get; set; }

    /// <summary>Counting of key presses per control (so a layout can follow what is used). Off by default, needs <see cref="Enabled"/>.</summary>
    public bool PressCounting { get; set; }

    /// <summary>Things that were already offered once (<c>template:obs-streaming</c>, <c>game:eldenring</c>): a profile the user deleted is never made again.</summary>
    public List<string> Offered { get; set; } = new();

    /// <summary>Suggestions that were answered, either way. A suggestion is never shown twice.</summary>
    public List<string> DismissedNudges { get; set; } = new();

    /// <summary>Prepared profiles the user has not looked at yet (the "new" mark).</summary>
    public List<string> UnseenProfileIds { get; set; } = new();

    /// <summary>
    /// Kinds of module the user has had attached (<c>keys</c>, <c>knobs</c>, <c>faders</c>…). <c>null</c> = never recorded: the first
    /// time it is recorded the modules that are connected then are taken as "known" and do not trigger a suggestion.
    /// </summary>
    public List<string>? SeenModules { get; set; }

    internal AssistSettings Copy() => new()
    {
        Asked = Asked,
        Enabled = Enabled,
        PressCounting = PressCounting,
        Offered = Offered.ToList(),
        DismissedNudges = DismissedNudges.ToList(),
        UnseenProfileIds = UnseenProfileIds.ToList(),
        SeenModules = SeenModules?.ToList()
    };
}

/// <summary>Read and change <see cref="AssistSettings"/> from any thread (the usage timer, the UI) without losing a change.</summary>
public interface IAssistSettingsStore
{
    AssistSettings Read();

    /// <summary>The master switch is on (cheap: no copy).</summary>
    bool Enabled { get; }

    /// <summary>The master switch and press counting are both on (cheap: asked on every press).</summary>
    bool PressCountingOn { get; }

    /// <summary>Applies <paramref name="change"/> to the stored settings under a lock and saves them.</summary>
    void Update(Action<AssistSettings> change);

    /// <summary>Raised after a change was saved (on the thread that changed it).</summary>
    event Action? Changed;
}

/// <summary>Keeps the assistant's settings inside <c>settings.json</c>, next to the other app settings.</summary>
public sealed class AssistSettingsStore : IAssistSettingsStore
{
    private readonly IProfileService _profiles;
    private readonly object _lock = new();

    public AssistSettingsStore(IProfileService profiles) => _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

    public event Action? Changed;

    public bool Enabled => _profiles.GetAppSettings().Assist is { Enabled: true };

    public bool PressCountingOn => _profiles.GetAppSettings().Assist is { Enabled: true, PressCounting: true };

    public AssistSettings Read()
    {
        lock (_lock) return (_profiles.GetAppSettings().Assist ?? new AssistSettings()).Copy();
    }

    public void Update(Action<AssistSettings> change)
    {
        lock (_lock)
        {
            var app = _profiles.GetAppSettings();
            var assist = app.Assist ?? new AssistSettings();
            change(assist);
            app.Assist = assist;
            _profiles.SaveAppSettings(app);
        }

        Changed?.Invoke();
    }
}
