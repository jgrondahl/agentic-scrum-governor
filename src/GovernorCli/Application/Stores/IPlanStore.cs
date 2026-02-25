using GovernorCli.Application.Models;

namespace GovernorCli.Application.Stores;

/// <summary>
/// Abstraction for persisting and retrieving approved implementation plans.
/// Plans are stored at: state/plans/item-{itemId}/implementation.plan.json
/// </summary>
public interface IPlanStore
{
    /// <summary>
    /// Persist an approved implementation plan.
    /// Creates directory structure as needed.
    /// Overwrites existing plan for the same item.
    /// </summary>
    /// <param name="workdir">Repository root</param>
    /// <param name="itemId">Backlog item ID</param>
    /// <param name="plan">Implementation plan to persist</param>
    void SavePlan(string workdir, int itemId, ImplementationPlan plan);

    /// <summary>
    /// Load an approved implementation plan if it exists.
    /// </summary>
    /// <param name="workdir">Repository root</param>
    /// <param name="itemId">Backlog item ID</param>
    /// <returns>Implementation plan, or null if not found</returns>
    ImplementationPlan? LoadPlan(string workdir, int itemId);

    /// <summary>
    /// Get the path where an approved plan would be stored.
    /// Does not check if the file exists.
    /// </summary>
    /// <param name="workdir">Repository root</param>
    /// <param name="itemId">Backlog item ID</param>
    /// <returns>Absolute path to state/plans/item-{itemId}/implementation.plan.json</returns>
    string GetPlanPath(string workdir, int itemId);
}
