namespace Launcher.Application.Accounts;

public sealed class LaunchAccountSessionException : Exception
{
    public LaunchAccountSessionException()
    {
    }

    public LaunchAccountSessionException(string message)
        : base(message)
    {
    }

    public LaunchAccountSessionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
