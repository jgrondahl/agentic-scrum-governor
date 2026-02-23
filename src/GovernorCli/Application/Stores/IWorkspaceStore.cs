namespace GovernorCli.Application.Stores;

/// <summary>
/// Abstraction for workspace lifecycle management.
/// Responsible for creating/resetting isolated workspaces per app.
/// Ensures determinism by resetting before each run.
/// </summary>
public interface IWorkspaceStore
{
    /// <summary>
    /// Reset and create workspace directory for an application.
    /// Deletes existing workspace (if present) to ensure determinism.
    /// Creates fresh state/workspaces/{appId}/ structure.
    /// </summary>
    /// <param name="workdir">Repository root directory</param>
    /// <param name="appId">Application identifier</param>
    /// <returns>Absolute path to workspace root</returns>
    string ResetAndCreateWorkspace(string workdir, string appId);
}
