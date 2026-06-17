namespace Launcher.Application.Services;

public sealed class InstanceRepairException : Exception
{
    public InstanceRepairException()
    {
    }

    public InstanceRepairException(string message)
        : base(message)
    {
    }

    public InstanceRepairException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
