namespace Launcher.Application.Accounts;

public sealed class AccountCapeOption
{
    public string? Id { get; init; }
    public required string DisplayName { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsActive { get; init; }
    public bool IsNone { get; init; }

    public string StateText => IsNone
        ? "\u4e0d\u4f7f\u7528\u62ab\u98ce"
        : IsActive ? "\u5f53\u524d\u4f7f\u7528" : "\u53ef\u7528";

    public override string ToString()
    {
        return DisplayName;
    }
}
