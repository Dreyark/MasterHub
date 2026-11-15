using System;
using System.Linq;
using System.Threading.Tasks;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles.Packages;

namespace MasterHub.Avalonia.ViewModels.Pages;

/// <summary>Importing a <c>.mhdeck</c> package (T-09): checked first, and anything that can run a program is shown before it is saved.</summary>
public partial class ProfilesPageViewModel
{
    private async Task ImportPackageAsync(string path)
    {
        var loc = LocalizationService.Instance;
        try
        {
            var preview = _packages!.Inspect(path);

            if (preview.Risks.Count > 0)
            {
                bool allowed = await _confirmDialog.ConfirmAsync(ConfirmRequests.ImportPrograms(preview.Risks), TimeSpan.FromMinutes(2));
                if (!allowed)
                {
                    StatusIsError = false;
                    StatusMessage = loc.GetString("Profiles_ImportCancelled", "Import cancelled – nothing was saved.");
                    return;
                }
            }

            var imported = _packages.Commit(preview);
            RefreshProfiles();
            var item = Profiles.FirstOrDefault(p => p.Id == imported.Id);
            if (item != null) ActivateProfile(item);

            StatusIsError = false;
            StatusMessage = preview.MissingPictures > 0
                ? string.Format(loc.GetString("Profiles_ImportedMissing", "Profile imported. {0} picture(s) were not in the package; those keys keep their icon and label."), preview.MissingPictures)
                : loc.GetString("Profiles_ImportSuccess", "Pomyślnie zaimportowano profil.");
            await RunCheckAsync(imported);   // T-34 #9: what this PC lacks for the imported profile
        }
        catch (PackageException ex)
        {
            StatusIsError = true;
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusMessage = loc.GetString("Profiles_ImportError", "Błąd podczas importu profilu: ") + ex.Message;
        }
    }
}
