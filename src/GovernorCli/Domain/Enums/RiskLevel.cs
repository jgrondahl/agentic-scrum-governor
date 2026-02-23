namespace GovernorCli.Domain.Enums;

/// <summary>
/// Risk levels for technical estimates and backlog items.
/// </summary>
public enum RiskLevel
{
    /// <summary>
    /// Low risk: well-understood technology, minimal dependencies.
    /// </summary>
    Low,

    /// <summary>
    /// Medium risk: typical for standard implementations with some unknowns.
    /// </summary>
    Medium,

    /// <summary>
    /// High risk: significant unknowns, new technology, or critical dependencies.
    /// </summary>
    High
}
