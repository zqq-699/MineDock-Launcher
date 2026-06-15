using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests;

internal sealed class FakeGameInstanceService : IGameInstanceService
{
    private readonly object syncRoot = new();
    public List<GameInstance> CreatedInstances { get; } = [];
    public Exception? CreateException { get; init; }
    public TaskCompletionSource<bool> CreateStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task? WaitBeforeCreate { get; init; }
    public string? LastMinecraftVersion { get; private set; }
    public LoaderKind LastLoader { get; private set; }
    public string? LastLoaderVersion { get; private set; }
    public string? LastName { get; private set; }
    public string? LastDefaultInstanceId { get; private set; }
    public int CreateCallCount { get; private set; }
    public int GetInstancesCallCount { get; private set; }

    public Task<IReadOnlyList<GameInstance>> GetInstancesAsync(CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            GetInstancesCallCount++;
            return Task.FromResult<IReadOnlyList<GameInstance>>(CreatedInstances.ToList());
        }
    }

    public Task<GameInstance?> GetDefaultInstanceAsync(CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            return Task.FromResult<GameInstance?>(CreatedInstances.FirstOrDefault());
        }
    }

    public async Task<GameInstance> CreateInstanceAsync(
        string minecraftVersion,
        LoaderKind loader,
        string? loaderVersion,
        string? name,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        LastMinecraftVersion = minecraftVersion;
        LastLoader = loader;
        LastLoaderVersion = loaderVersion;
        LastName = name;
        progress?.Report(new LauncherProgress("Install", "Downloading", 25));
        CreateStarted.TrySetResult(true);
        lock (syncRoot)
        {
            CreateCallCount++;
        }

        if (WaitBeforeCreate is not null)
            await WaitBeforeCreate;

        if (CreateException is not null)
            throw CreateException;

        var instance = new GameInstance
        {
            Name = string.IsNullOrWhiteSpace(name) ? minecraftVersion : name,
            MinecraftVersion = minecraftVersion,
            Loader = loader,
            LoaderVersion = loaderVersion,
            VersionName = minecraftVersion,
            InstanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"))
        };
        lock (syncRoot)
        {
            CreatedInstances.Add(instance);
        }
        return instance;
    }

    public Task SaveInstanceAsync(GameInstance instance, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<bool> SetDefaultInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            LastDefaultInstanceId = instanceId;
            return Task.FromResult(CreatedInstances.Any(instance => instance.Id == instanceId));
        }
    }
}
