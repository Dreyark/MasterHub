using System.Text;
using MasterHub.Core.Profiles.Packages;

namespace MasterHub.Core.Rendering.Icons;

/// <summary>
/// Turns a library icon into a picture file of the key (T-21a). The tinted SVG goes into the media store under a name made
/// from its content, so the key uses the normal picture path: the renderer, the active state, zoom/move, backups and
/// <c>.mhdeck</c> packages all work with it without knowing that it came from the library.
/// </summary>
public static class IconFiles
{
    /// <summary>The path of the stored SVG, or <c>null</c> for an unknown icon.</summary>
    public static string? Save(IIconLibrary library, IMediaStore store, string name, string tintHex, int paddingPercent = 18)
    {
        string? svg = library.BuildSvg(name, tintHex, paddingPercent);
        return svg == null ? null : store.ImportBytes(Encoding.UTF8.GetBytes(svg), ".svg");
    }
}
