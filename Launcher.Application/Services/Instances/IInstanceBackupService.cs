using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IInstanceBackupService
{
    Task<string> EnsureBackupDirectoryAsync(string backupDirectory, CancellationToken cancellationToken = default);

    Task<int> CountBackupEntriesAsync(string backupDirectory, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InstanceBackupRecord>> GetBackupsAsync(string backupDirectory, CancellationToken cancellationToken = default);

    Task<InstanceBackupRecord> CreateBackupAsync(
        GameInstance instance,
        string backupDirectory,
        string backupName,
        CancellationToken cancellationToken = default);

    Task DeleteBackupAsync(
        string backupDirectory,
        string backupFullPath,
        CancellationToken cancellationToken = default);

    Task RestoreBackupAsync(
        GameInstance instance,
        string backupDirectory,
        string backupFullPath,
        CancellationToken cancellationToken = default);
}
