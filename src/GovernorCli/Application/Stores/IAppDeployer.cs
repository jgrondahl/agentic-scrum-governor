using GovernorCli.Application.Models.Deliver;

namespace GovernorCli.Application.Stores;

/// <summary>
/// Abstraction for deploying approved implementations to repository.
/// Responsible for copying workspace candidate to /apps/{appId}/ on approval.
/// Returns typed PatchFile records for auditability.
/// Minimal overwrite semantics: no rollback complexity.
/// </summary>
public interface IAppDeployer
{
    /// <summary>
    /// Deploy workspace candidate to repository target.
    /// Copies from workspace/<appId>/ to /apps/<appId>/ (overwrites allowed).
    /// Returns typed PatchFile records with action, path, size, hash.
    /// </summary>
    /// <param name="workdir">Repository root directory</param>
    /// <param name="workspaceRoot">Workspace root path (state/workspaces/{appId}/)</param>
    /// <param name="appId">Application identifier</param>
    /// <returns>List of typed PatchFile records for deployed files</returns>
    List<PatchFile> Deploy(string workdir, string workspaceRoot, string appId);
}
