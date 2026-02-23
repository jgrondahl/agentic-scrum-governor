namespace GovernorCli.Domain.Enums;

/// <summary>
/// Backlog item status values representing the lifecycle state of an item.
/// Follows the SDLC phases: Intake → Refine → Refine-Tech → Deliver → Done.
/// </summary>
public enum ItemStatus
{
    /// <summary>
    /// Initial state after intake, awaiting business refinement.
    /// </summary>
    Candidate,

    /// <summary>
    /// Business-refined, awaiting technical readiness review (TRR).
    /// </summary>
    Ready,

    /// <summary>
    /// Technically ready (TRR complete), awaiting sprint assignment.
    /// </summary>
    ReadyForDev,

    /// <summary>
    /// Assigned to active sprint, in development.
    /// </summary>
    InSprint,

    /// <summary>
    /// Completed and deployed.
    /// </summary>
    Done
}
