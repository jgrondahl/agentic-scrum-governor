namespace GovernorCli.Domain.Exceptions;

/// <summary>
/// Raised when a backlog item cannot be found by ID.
/// </summary>
public class ItemNotFoundException : Exception
{
    public int ItemId { get; }

    public ItemNotFoundException(int itemId)
        : base($"Backlog item with ID {itemId} not found.")
    {
        ItemId = itemId;
    }
}
