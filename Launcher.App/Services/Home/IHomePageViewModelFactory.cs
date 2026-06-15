using Launcher.Domain.Models;

namespace Launcher.App.Services;

public interface IHomePageViewModelFactory
{
    HomePageViewModel Create(
        AccountPageViewModel accountPage,
        Action<string> navigateToPage,
        Action<double> reportProgressPercent,
        Func<GameInstance, Task<bool>> selectLaunchInstance);
}


