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
    private readonly IUiDispatcher uiDispatcher;

    public HomePageViewModelFactory(
        ILaunchService launchService,
        IGameVersionService gameVersionService,
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        IWindowService windowService,
        IUiDispatcher uiDispatcher)
    {
        this.launchService = launchService;
        this.gameVersionService = gameVersionService;
        this.statusService = statusService;
        this.floatingMessageService = floatingMessageService;
        this.windowService = windowService;
        this.uiDispatcher = uiDispatcher;
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
            uiDispatcher,
            reportProgressPercent,
            selectLaunchInstance,
            openGameSettingsForInstance);
    }
}


