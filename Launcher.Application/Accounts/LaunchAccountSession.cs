namespace Launcher.Application.Accounts;

public sealed record LaunchAccountSession(
    string Username,
    string AccessToken,
    string Uuid,
    bool IsOffline);
