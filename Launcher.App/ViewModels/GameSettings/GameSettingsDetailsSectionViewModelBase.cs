using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public abstract class GameSettingsDetailsSectionViewModelBase : ObservableObject
{
    protected GameSettingsDetailsSectionViewModelBase(GameSettingsDetailsViewModel parent)
    {
        Parent = parent;
    }

    public GameSettingsDetailsViewModel Parent { get; }

    public virtual void OnSelectedInstanceChanged(GameInstance? instance)
    {
    }

    public virtual void OnSectionDeactivated()
    {
    }

    public virtual Task OnSectionActivatedAsync()
    {
        return Task.CompletedTask;
    }
}
