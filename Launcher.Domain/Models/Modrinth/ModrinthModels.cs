namespace Launcher.Domain.Models;

public sealed class ModrinthProject
{
    public string ProjectId { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public int Downloads { get; set; }
}
