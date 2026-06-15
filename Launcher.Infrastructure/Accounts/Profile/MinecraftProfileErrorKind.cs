namespace Launcher.Infrastructure.Accounts;

internal enum MinecraftProfileErrorKind
{
    Unknown = 0,
    Duplicate,
    NotAllowed,
    ConstraintViolation
}
