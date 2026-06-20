using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Fakes;

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
    public DownloadSourcePreference LastDownloadSourcePreference { get; private set; } = DownloadSourcePreference.Auto;
    public int LastDownloadSpeedLimitMbPerSecond { get; private set; }
    public string? LastDefaultInstanceId { get; private set; }
    public string? LastDeletedInstanceId { get; private set; }
    public string? LastRenamedInstanceId { get; private set; }
    public string? LastRenamedName { get; private set; }
    public string? LastRenamedIconSource { get; private set; }
    public GameInstance? LastSavedInstance { get; private set; }
    public int CreateCallCount { get; private set; }
    public int GetInstancesCallCount { get; private set; }
    public int DeleteCallCount { get; private set; }
    public int SaveCallCount { get; private set; }
    public LauncherProgress? InitialProgress { get; init; }

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
        CancellationToken cancellationToken = default,
        DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        LastMinecraftVersion = minecraftVersion;
        LastLoader = loader;
        LastLoaderVersion = loaderVersion;
        LastName = name;
        LastDownloadSourcePreference = downloadSourcePreference;
        LastDownloadSpeedLimitMbPerSecond = downloadSpeedLimitMbPerSecond;
        progress?.Report(InitialProgress ?? new LauncherProgress(InstallProgressStages.Preparing, string.Empty, 25));
        CreateStarted.TrySetResult(true);
        lock (syncRoot)
        {
            CreateCallCount++;
        }

        if (WaitBeforeCreate is not null)
            await WaitBeforeCreate.WaitAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

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
        lock (syncRoot)
        {
            SaveCallCount++;
            LastSavedInstance = instance;

            var index = CreatedInstances.FindIndex(existing => existing.Id == instance.Id);
            if (index >= 0)
                CreatedInstances[index] = instance;
            else
                CreatedInstances.Add(instance);
        }

        return Task.CompletedTask;
    }

    public Task<GameInstance> RenameInstanceAsync(
        string instanceId,
        string? newName,
        string? newIconSource,
        CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            LastRenamedInstanceId = instanceId;
            LastRenamedName = newName;
            LastRenamedIconSource = newIconSource;

            var instance = CreatedInstances.First(existing => existing.Id == instanceId);
            instance.Name = string.IsNullOrWhiteSpace(newName) ? instance.Name : newName.Trim();
            instance.VersionName = string.IsNullOrWhiteSpace(newName) ? instance.VersionName : newName.Trim();
            instance.IconSource = string.IsNullOrWhiteSpace(newIconSource) ? null : newIconSource.Trim();
            instance.InstanceDirectory = Path.Combine(Path.GetDirectoryName(instance.InstanceDirectory) ?? Path.GetTempPath(), instance.VersionName);
            return Task.FromResult(instance);
        }
    }

    public Task<bool> SetDefaultInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            LastDefaultInstanceId = instanceId;
            return Task.FromResult(CreatedInstances.Any(instance => instance.Id == instanceId));
        }
    }

    public Task<bool> DeleteInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            LastDeletedInstanceId = instanceId;
            DeleteCallCount++;
            var removed = CreatedInstances.RemoveAll(instance => instance.Id == instanceId) > 0;
            return Task.FromResult(removed);
        }
    }
}

