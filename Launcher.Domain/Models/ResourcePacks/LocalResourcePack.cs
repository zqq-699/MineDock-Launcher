namespace Launcher.Domain.Models;

public sealed class LocalResourcePack
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string? IconSource { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
