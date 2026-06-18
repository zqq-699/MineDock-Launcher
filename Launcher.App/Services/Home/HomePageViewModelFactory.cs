using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.Services;

public sealed class HomePageViewModelFactory : IHomePageViewModelFactory
{
    private readonly ILaunchService launchService;
    private readonly IGameVersionService gameVersionService;
    private readonly IStatusService statusService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly IWindowService windowService;

    public HomePageViewModelFactory(
        ILaunchService launchService,
        IGameVersionService gameVersionService,
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        IWindowService windowService)
    {
        this.launchService = launchService;
        this.gameVersionService = gameVersionService;
        this.statusService = statusService;
        this.floatingMessageService = floatingMessageService;
        this.windowService = windowService;
    }

    public HomePageViewModel Create(
        AccountPageViewModel accountPage,
        Action<double> reportProgressPercent,
        Func<GameInstance, Task<bool>> selectLaunchInstance,
        Func<GameInstance?, Task> openGameSettingsForInstance)
    {
        return new HomePageViewModel(
            launchService,
            gameVersionService,
            accountPage,
            statusService,
            floatingMessageService,
            windowService,
            reportProgressPercent,
            selectLaunchInstance,
            openGameSettingsForInstance);
    }
}


