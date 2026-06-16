using Launcher.Domain.Models;

namespace Launcher.Application.Repositories;

public interface IGameInstanceRepository
{
    Task<IReadOnlyList<GameInstance>> GetAllAsync(CancellationToken cancellationToken = default);

    Task SaveAllAsync(IReadOnlyCollection<GameInstance> instances, CancellationToken cancellationToken = default);

    string GetUniqueInstanceDirectory(string dataDirectory, string name);

    string GetVersionDirectory(string minecraftDirectory, string versionName);

    bool IsInstanceInstalled(GameInstance instance, string minecraftDirectory);

    void CreateInstanceDirectories(string directory);

    void DeleteVersionDirectory(string minecraftDirectory, string versionName);
}
