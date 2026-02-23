namespace GovernorCli.Domain.Exceptions;

/// <summary>
/// Raised when backlog.yaml cannot be parsed or loaded.
/// </summary>
public class BacklogParseException : Exception
{
    public string FilePath { get; }

    public BacklogParseException(string filePath, string message)
        : base($"Failed to parse backlog at {filePath}: {message}")
    {
        FilePath = filePath;
    }

    public BacklogParseException(string filePath, Exception innerException)
        : base($"Failed to parse backlog at {filePath}", innerException)
    {
        FilePath = filePath;
    }
}
