namespace Launcher.App.Models;

public sealed class AccountTypeOption
{
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
}
