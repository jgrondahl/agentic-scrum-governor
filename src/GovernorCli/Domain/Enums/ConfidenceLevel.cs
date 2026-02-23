namespace GovernorCli.Domain.Enums;

/// <summary>
/// Estimation confidence levels indicating the certainty of the estimate.
/// </summary>
public enum ConfidenceLevel
{
    /// <summary>
    /// Low confidence: significant unknowns or assumptions.
    /// </summary>
    Low,

    /// <summary>
    /// Medium confidence: typical for initial estimates with some unknowns.
    /// </summary>
    Medium,

    /// <summary>
    /// High confidence: well-understood requirements and scope.
    /// </summary>
    High
}
