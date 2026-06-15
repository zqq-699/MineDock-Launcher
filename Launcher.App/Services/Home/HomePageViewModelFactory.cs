using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.Services;

public sealed class HomePageViewModelFactory : IHomePageViewModelFactory
{
    private readonly ILaunchService launchService;
    private readonly IGameVersionService gameVersionService;
    private readonly IStatusService statusService;

    public HomePageViewModelFactory(
        ILaunchService launchService,
        IGameVersionService gameVersionService,
        IStatusService statusService)
    {
        this.launchService = launchService;
        this.gameVersionService = gameVersionService;
        this.statusService = statusService;
    }

    public HomePageViewModel Create(
        AccountPageViewModel accountPage,
        Action<string> navigateToPage,
        Action<double> reportProgressPercent,
        Func<GameInstance, Task<bool>> selectLaunchInstance)
    {
        return new HomePageViewModel(
            launchService,
            gameVersionService,
            accountPage,
            statusService,
            navigateToPage,
            reportProgressPercent,
            selectLaunchInstance);
    }
}


