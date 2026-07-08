using Launcher.App.ViewModels.Home;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.Services;

public sealed class HomePageViewModelFactory : IHomePageViewModelFactory
{
    private readonly ILaunchService launchService;
    private readonly IGameVersionService gameVersionService;
    private readonly IStatusService statusService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly IWindowService windowService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<HomePageViewModel> logger;

    public HomePageViewModelFactory(
        ILaunchService launchService,
        IGameVersionService gameVersionService,
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        IWindowService windowService,
        IUiDispatcher uiDispatcher,
        ILogger<HomePageViewModel>? logger = null)
    {
        this.launchService = launchService;
        this.gameVersionService = gameVersionService;
        this.statusService = statusService;
        this.floatingMessageService = floatingMessageService;
        this.windowService = windowService;
        this.uiDispatcher = uiDispatcher;
        this.logger = logger ?? NullLogger<HomePageViewModel>.Instance;
    }

    public HomePageViewModel Create(
        AccountPageViewModel accountPage,
        Action<double> reportProgressPercent,
        Func<GameInstance, Task<bool>> selectLaunchInstance,
        Func<bool, Task<bool>> setLaunchMenuPinned,
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
            setLaunchMenuPinned,
            openGameSettingsForInstance,
            logger);
    }
}
