namespace Launcher.Application.Services;

public sealed class JavaRuntimeSelectionException : Exception
{
    public JavaRuntimeSelectionException(string message)
        : base(message)
    {
    }

    public JavaRuntimeSelectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
