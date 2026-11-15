using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Api.Client;

/// <summary>How one check of <c>doctor</c> came out.</summary>
public enum CheckStatus
{
    Ok = 0,

    /// <summary>Works, but something is worth a look (no deck module connected, a newer API).</summary>
    Warning = 1,

    /// <summary>Does not work: <c>doctor</c> exits with 1.</summary>
    Failed = 2
}

public sealed record DoctorCheck(CheckStatus Status, string Title, string? Detail = null);

/// <summary>
/// The checks behind <c>masterhub-cli doctor</c>: is the app running, does it accept the token, is a profile active, is a deck connected.
/// Written as a list of results (not printed text) so the diagnostics page (T-29) can show and extend the same checks.
/// </summary>
public static class Doctor
{
    /// <param name="connect">Opens the connection (reads port and token); throws <see cref="MasterHubNotRunningException"/> when the app is closed.</param>
    public static async Task<IReadOnlyList<DoctorCheck>> RunAsync(Func<ILocalApiClient> connect, CancellationToken ct = default)
    {
        var checks = new List<DoctorCheck>();

        ILocalApiClient client;
        try
        {
            client = connect();
        }
        catch (MasterHubNotRunningException ex)
        {
            checks.Add(new DoctorCheck(CheckStatus.Failed, "MasterHub is running", ex.Message));
            return checks;
        }

        using (client)
        {
            ApiVersion version;
            try
            {
                version = await client.GetVersionAsync(ct).ConfigureAwait(false);
                checks.Add(new DoctorCheck(CheckStatus.Ok, "MasterHub is running", $"version {version.Version}, port {client.Connection.Port}"));
            }
            catch (MasterHubNotRunningException ex)
            {
                checks.Add(new DoctorCheck(CheckStatus.Failed, "MasterHub is running", ex.Message));
                return checks;
            }
            catch (ApiException ex) when (ex.Status == 401)
            {
                checks.Add(new DoctorCheck(CheckStatus.Failed, "The app accepts the token", ex.Message));
                return checks;
            }
            catch (ApiException ex)
            {
                checks.Add(new DoctorCheck(CheckStatus.Failed, "MasterHub is running", ex.Message));
                return checks;
            }

            checks.Add(new DoctorCheck(CheckStatus.Ok, "The app accepts the token"));

            if (version.Api != ApiVersion.Current)
            {
                checks.Add(new DoctorCheck(CheckStatus.Warning, "The API version matches",
                    $"the app speaks API {version.Api}, this CLI speaks API {ApiVersion.Current}: update the older one"));
            }
            else
            {
                checks.Add(new DoctorCheck(CheckStatus.Ok, "The API version matches", $"API {version.Api}"));
            }

            try
            {
                var state = await client.GetStateAsync(ct).ConfigureAwait(false);
                var active = state.Profiles.FirstOrDefault(p => p.Id == state.ActiveProfileId);
                checks.Add(active == null
                    ? new DoctorCheck(CheckStatus.Warning, "A profile is active", "the active profile is not in the list (a draft is on the deck?)")
                    : new DoctorCheck(CheckStatus.Ok, "A profile is active", $"\"{active.Name}\", {state.Profiles.Count} profile(s) in all"));
            }
            catch (ApiException ex)
            {
                checks.Add(new DoctorCheck(CheckStatus.Failed, "A profile is active", ex.Message));
            }

            try
            {
                var modules = await client.GetModulesAsync(ct).ConfigureAwait(false);
                var kinds = modules.ConnectedKinds();
                checks.Add(kinds.Count == 0
                    ? new DoctorCheck(CheckStatus.Warning, "A deck is connected", "no module is connected (the app works without one, keys show only in its window)")
                    : new DoctorCheck(CheckStatus.Ok, "A deck is connected", string.Join(", ", kinds)));
            }
            catch (ApiException ex)
            {
                checks.Add(new DoctorCheck(CheckStatus.Failed, "A deck is connected", ex.Message));
            }
        }

        return checks;
    }

    /// <summary>The CLI exits with 1 when any check failed (a warning is not a failure).</summary>
    public static bool Passed(IEnumerable<DoctorCheck> checks) => checks.All(c => c.Status != CheckStatus.Failed);
}
