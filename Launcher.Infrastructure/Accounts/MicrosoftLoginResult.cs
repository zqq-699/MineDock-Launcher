using CmlLib.Core.Auth.Microsoft.Sessions;

namespace Launcher.Infrastructure.Accounts;

internal sealed record MicrosoftLoginResult(
    JEProfile? Profile,
    string? Username,
    string? Uuid);
