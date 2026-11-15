using Microsoft.Extensions.DependencyInjection;
using MasterHub.Avalonia.ViewModels.Inspector;
using MasterHub.Core.Profiles.Packages;
using MasterHub.Core.Rendering.Icons;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>The icon library picker of the key (T-21a).</summary>
public partial class KeyItemViewModel
{
    private IconPickerViewModel? _iconPicker;

    /// <summary>Made the first time the picker is opened (reading the library costs a moment).</summary>
    public IconPickerViewModel IconPicker
    {
        get
        {
            if (_iconPicker == null)
            {
                _iconPicker = new IconPickerViewModel();
                _iconPicker.Picked += (name, tint) => ApplyLibraryIcon(name, tint);
            }
            return _iconPicker;
        }
    }

    /// <summary>
    /// Puts a library icon on the key (on the state being edited): the tinted SVG is stored in the media store and used as
    /// the key picture, fitted inside the key. Returns <c>false</c> for an unknown icon.
    /// </summary>
    public bool ApplyLibraryIcon(string name, string tintHex, IIconLibrary? library = null, IMediaStore? store = null)
    {
        library ??= IconLibrary.Shared;
        store ??= App.Services?.GetService<IMediaStore>() ?? new MediaStore();

        string? path;
        try
        {
            path = IconFiles.Save(library, store, name, tintHex);
        }
        catch
        {
            path = null;
        }
        if (path == null) return false;

        MediaValidationMessage = null;
        if (SelectedEditState == 1)
        {
            ActiveImagePath = path;
            ActiveImageFitMode = "Contain";
        }
        else
        {
            ImagePath = path;
            ImageFitMode = "Contain";
        }
        NotifyCurrentEditProperties();
        ActionChangedCallback?.Invoke(this);
        return true;
    }
}
