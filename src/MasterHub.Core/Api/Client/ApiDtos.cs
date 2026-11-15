using System.Collections.Generic;

namespace MasterHub.Core.Api.Client;

/// <summary>Answer of <c>GET /api/version</c>.</summary>
/// <param name="App">Always <c>MasterHub</c>.</param>
/// <param name="Version">Version of the running app.</param>
/// <param name="Api">Version of the local API; it grows only when an existing route changes in an incompatible way.</param>
public sealed record ApiVersion(string App, string Version, int Api)
{
    public const int Current = 1;
}

public sealed record ApiProfile(string Id, string Name, bool IsDefault);

public sealed record ApiForeground(string Process, string Title);

/// <summary>Answer of <c>GET /api/state</c>.</summary>
public sealed record ApiState(string ActiveProfileId, List<ApiProfile> Profiles, ApiForeground? Foreground, bool? AutoSwitch);

public sealed record ApiDevice(string Name, string Serial, bool Connected);

/// <summary>Answer of <c>GET /api/modules</c>.</summary>
public sealed record ApiModules(bool Keys, bool Faders, bool Rollers, bool Knobs, bool Dial, bool BaseStation, List<ApiDevice> Devices)
{
    /// <summary>The module kinds that are connected, in the order the CLI prints them.</summary>
    public IReadOnlyList<string> ConnectedKinds()
    {
        var kinds = new List<string>();
        if (Keys) kinds.Add("keys");
        if (Knobs) kinds.Add("knobs");
        if (Faders) kinds.Add("faders");
        if (Rollers) kinds.Add("rollers");
        if (Dial) kinds.Add("dial");
        if (BaseStation) kinds.Add("base");
        return kinds;
    }
}

/// <summary>Answer of <c>POST /api/profiles/{id}/controls</c>.</summary>
public sealed record ControlsResult(int Changed, List<string>? Warnings);
