using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MasterHub.Core.Apps;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.HomeAssistant;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Profiles.Check;

namespace MasterHub.Avalonia.Services;

/// <summary>
/// Builds the lookups of the profile check (T-34 #9) from the running services. A service that is missing, not connected
/// or fails leaves its part <c>null</c>, so those controls are not marked (OBS scenes are only checked while OBS is connected).
/// </summary>
public static class ProfileCheckEnvironments
{
    public static async Task<IProfileCheckEnvironment> FromServicesAsync(IServiceProvider? services)
    {
        var installed = Try(() => services?.GetService<IInstalledAppsService>()?.Apps.Select(a => a.ExePath is { Length: > 0 } exe ? System.IO.Path.GetFileName(exe) : a.Name).ToList());
        var devices = Try(() => services?.GetService<IAudioDeviceService>()?.GetAllDevices());
        var ha = services?.GetService<IHomeAssistantService>();
        var entities = ha is { IsConnected: true } ? Try(() => ha.Entities.Select(e => e.EntityId).ToList()) : null;
        var host = services?.GetService<IStreamDeckPluginHost>();
        var plugins = host == null ? null : Try(() => host.InstalledPlugins.Select(p => p.PluginUuid).ToList());

        System.Collections.Generic.IReadOnlyList<string>? scenes = null;
        var obs = services?.GetService<IObsService>();
        if (obs is { IsConnected: true })
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                scenes = await obs.GetSceneListAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
                scenes = null;   // a slow / broken OBS: scenes are not checked
            }
        }

        return new SystemProfileCheckEnvironment(installed)
        {
            AudioDevices = devices,
            ObsScenes = scenes,
            HomeAssistantEntities = entities,
            InstalledPlugins = plugins
        };
    }

    private static T? Try<T>(Func<T?> f) where T : class
    {
        try
        {
            return f();
        }
        catch
        {
            return null;
        }
    }
}
