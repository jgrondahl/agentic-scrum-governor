namespace GovernorCli.Domain.Exceptions;

/// <summary>
/// Thrown when an attempt is made to execute a process that is not allowlisted.
/// ProcessRunner only permits: dotnet build, dotnet run
/// </summary>
public class ForbiddenProcessException : Exception
{
    public ForbiddenProcessException(string message) : base(message)
    {
    }
}
