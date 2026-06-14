using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Launcher.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLauncherApplication(this IServiceCollection services)
    {
        services.AddSingleton<IAccountStore, AccountStore>();
        services.AddSingleton<IGameInstanceService, GameInstanceService>();
        return services;
    }
}
