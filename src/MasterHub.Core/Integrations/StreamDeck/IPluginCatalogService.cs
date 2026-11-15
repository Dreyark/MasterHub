using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Integrations.StreamDeck;

public interface IPluginCatalogService : IDisposable
{
    Task<IReadOnlyList<PluginCatalogEntry>> GetCatalogAsync(bool forceRefresh = false, CancellationToken ct = default);
    Task<string?> DownloadPluginPackageAsync(PluginCatalogEntry entry, CancellationToken ct = default);
}
