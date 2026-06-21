namespace Launcher.Domain.Models;

public sealed class LocalSave
{
    public string Name { get; set; } = string.Empty;
    public string DirectoryName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string? IconSource { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
