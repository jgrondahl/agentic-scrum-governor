namespace GovernorCli.Domain.Exceptions;

/// <summary>
/// Raised when patch application to backlog fails.
/// </summary>
public class PatchApplicationException : Exception
{
    public int ItemId { get; }
    public string RunId { get; }

    public PatchApplicationException(int itemId, string runId, string message)
        : base($"Failed to apply patch for item {itemId} (run {runId}): {message}")
    {
        ItemId = itemId;
        RunId = runId;
    }

    public PatchApplicationException(int itemId, string runId, Exception innerException)
        : base($"Failed to apply patch for item {itemId} (run {runId})", innerException)
    {
        ItemId = itemId;
        RunId = runId;
    }
}
