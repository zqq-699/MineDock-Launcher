using System.Collections.ObjectModel;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed class LocalModsViewModel
{
    private readonly IModService modService;
    private readonly IStatusService statusService;
    private GameInstance? selectedInstance;
    private int modRefreshVersion;

    public LocalModsViewModel(
        IModService modService,
        IStatusService statusService)
    {
        this.modService = modService;
        this.statusService = statusService;
    }

    public ObservableCollection<LocalMod> Mods { get; } = [];

    public Task SetSelectedInstanceAsync(GameInstance? instance)
    {
        selectedInstance = instance;
        return RefreshModsAsync();
    }

    public async Task RefreshModsAsync()
    {
        var refreshVersion = Interlocked.Increment(ref modRefreshVersion);
        var instance = selectedInstance;

        if (instance is null)
        {
            Mods.Clear();
            return;
        }

        var loadedMods = await modService.GetModsAsync(instance);
        if (refreshVersion != modRefreshVersion
            || !string.Equals(instance.Id, selectedInstance?.Id, StringComparison.Ordinal))
        {
            return;
        }

        Mods.ReplaceWith(loadedMods);
    }

    public async Task ToggleModAsync(LocalMod mod)
    {
        await modService.SetEnabledAsync(mod, !mod.IsEnabled);
        await RefreshModsAsync();
    }

    public async Task DeleteModAsync(LocalMod mod)
    {
        await modService.DeleteAsync(mod);
        await RefreshModsAsync();
    }

    public async Task ImportModFromPathAsync(string path)
    {
        if (selectedInstance is null || string.IsNullOrWhiteSpace(path))
            return;

        await modService.ImportAsync(selectedInstance, path);
        await RefreshModsAsync();
        ReportStatus(Strings.Status_LocalModImported);
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }
}

