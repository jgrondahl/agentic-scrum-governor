namespace GovernorCli.Application.Stores;

/// <summary>
/// Abstraction for governance decision logging.
/// Responsible for maintaining append-only decision log.
/// </summary>
public interface IDecisionStore
{
    /// <summary>
    /// Append a governance decision to the decision log.
    /// Append-only: entries are never modified or deleted.
    /// Format: TIMESTAMP | decision_type | context
    /// Example: 2024-01-15T10:30:00Z | refine-tech approved | item=42 | run=20240115_103000_refine-tech_item-42 | by=alice
    /// </summary>
    /// <param name="workdir">Repository root directory</param>
    /// <param name="entry">Decision entry (format: TIMESTAMP | decision_type | context)</param>
    void LogDecision(string workdir, string entry);
}
