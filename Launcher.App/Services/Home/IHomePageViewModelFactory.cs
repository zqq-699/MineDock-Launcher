using Launcher.Domain.Models;

namespace Launcher.App.Services;

public interface IHomePageViewModelFactory
{
    HomePageViewModel Create(
        AccountPageViewModel accountPage,
        Action<double> reportProgressPercent,
        Func<GameInstance, Task<bool>> selectLaunchInstance,
        Func<GameInstance?, Task> openGameSettingsForInstance);
}


