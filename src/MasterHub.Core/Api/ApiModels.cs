using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MasterHub.Core.Api;

/// <summary>JSON conventions of the local API (camelCase, enums as strings).</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>
/// What the API needs from the running application and cannot do on its own (UI-thread work, the Studio's in-memory
/// state). The desktop app implements it; tests use a fake.
/// </summary>
public interface ILocalApiHost
{
    /// <summary>Activates a profile in the app (updates the Studio and the profile list). Returns <c>false</c> for an unknown id.</summary>
    bool ActivateProfile(string profileId);

    /// <summary>Saves the Studio's in-memory edits first, so an API write is never overwritten by a later autosave.</summary>
    void FlushActiveProfile();

    /// <summary>Tells the app that a profile file was changed from outside (re-applies it when it is the active one).</summary>
    void ProfileChanged(string profileId);

    /// <summary>
    /// Sets the deck's lights for now (not saved in any profile): they stay until another profile is applied or <see cref="LightsMode.Profile"/>
    /// is asked for. Returns <c>false</c> when the app cannot do it. A host that does not know lights keeps the default.
    /// </summary>
    bool SetLights(LightsMode mode, string? colorHex) => false;
}

/// <summary>What <c>POST /api/lights</c> asks the deck's lights to do.</summary>
public enum LightsMode
{
    /// <summary>Give the lights back to the active profile.</summary>
    Profile = 0,
    Off = 1,
    /// <summary>One static colour (needs <see cref="LightsRequest.Color"/>).</summary>
    Color = 2,
    Rainbow = 3
}

/// <summary>Body of <c>POST /api/lights</c>.</summary>
public sealed class LightsRequest
{
    /// <summary><c>profile</c>, <c>off</c>, <c>color</c> or <c>rainbow</c>.</summary>
    public string Mode { get; set; } = "";

    /// <summary><c>#rrggbb</c>, only with <c>color</c>.</summary>
    public string? Color { get; set; }
}

/// <summary>One requested change of one control (v1: keys).</summary>
public sealed class ControlChange
{
    /// <summary>Control id, e.g. <c>keys.3</c> (0-based).</summary>
    public string Control { get; set; } = "";

    /// <summary>Remove the binding (reset the key to empty).</summary>
    public bool Clear { get; set; }

    /// <summary>A <c>DeckActionType</c> name, e.g. <c>MediaPlayPause</c>.</summary>
    public string? Action { get; set; }

    public string? Parameter { get; set; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? BackgroundColorHex { get; set; }
    public string? TextColorHex { get; set; }

    /// <summary>1-based page of the profile the key is on; <c>null</c> = page 1.</summary>
    public int? Page { get; set; }
}

/// <summary>Body of <c>POST /api/profiles/{id}/controls</c>: all changes apply or none does.</summary>
public sealed class ControlsRequest
{
    public List<ControlChange> Controls { get; set; } = new();
}

/// <summary>Outcome of validating a set of changes.</summary>
public sealed class ValidationResult
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool IsValid => Errors.Count == 0;
}
