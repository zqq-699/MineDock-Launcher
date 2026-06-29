namespace Launcher.Domain.Models;

public sealed class InstanceBackupRecord
{
    public string Name { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
