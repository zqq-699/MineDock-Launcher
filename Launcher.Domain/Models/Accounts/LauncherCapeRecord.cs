namespace Launcher.Domain.Models;

public sealed class LauncherCapeRecord
{
    public string? Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
    public bool IsNone { get; set; }
}
