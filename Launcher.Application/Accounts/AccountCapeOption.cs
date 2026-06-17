namespace Launcher.Application.Accounts;

public sealed class AccountCapeOption
{
    public string? Id { get; init; }
    public required string DisplayName { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsActive { get; init; }
    public bool IsNone { get; init; }

    public override string ToString()
    {
        return DisplayName;
    }
}
