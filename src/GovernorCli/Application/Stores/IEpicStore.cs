namespace GovernorCli.Application.Stores;

/// <summary>
/// Abstraction for epic registry lookups.
/// Responsible for resolving epic_id to app_id from state/epics.yaml.
/// </summary>
public interface IEpicStore
{
    /// <summary>
    /// Resolve epic_id to app_id.
    /// </summary>
    /// <param name="workdir">Repository root directory</param>
    /// <param name="epicId">Epic identifier from backlog item</param>
    /// <returns>Application identifier</returns>
    /// <exception cref="FileNotFoundException">If state/epics.yaml does not exist</exception>
    /// <exception cref="KeyNotFoundException">If epic_id not found in registry</exception>
    string ResolveAppId(string workdir, string epicId);
}
