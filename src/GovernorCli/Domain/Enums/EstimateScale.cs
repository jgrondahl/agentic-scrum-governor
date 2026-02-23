namespace GovernorCli.Domain.Enums;

/// <summary>
/// Estimation scales used for story point calculations.
/// </summary>
public enum EstimateScale
{
    /// <summary>
    /// Fibonacci sequence: 1, 2, 3, 5, 8, 13, 21, etc.
    /// </summary>
    Fibonacci,

    /// <summary>
    /// Linear sequence: 1, 2, 3, 4, 5, etc.
    /// </summary>
    Linear,

    /// <summary>
    /// T-shirt sizing: XS, S, M, L, XL.
    /// </summary>
    TShirtSize,

    /// <summary>
    /// Planning poker: standard deck values.
    /// </summary>
    PlanningPoker
}
