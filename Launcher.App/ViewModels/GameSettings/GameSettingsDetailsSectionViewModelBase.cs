using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels.GameSettings;

public abstract class GameSettingsDetailsSectionViewModelBase : ObservableObject
{
    protected GameSettingsDetailsSectionViewModelBase(GameSettingsDetailsViewModel parent)
    {
        Parent = parent;
    }

    public GameSettingsDetailsViewModel Parent { get; }
}
