using GovernorCli.Application.Stores;

namespace GovernorCli.Infrastructure.Stores;

public class WorkspaceStore : IWorkspaceStore
{
    public string ResetAndCreateWorkspace(string workdir, string appId)
    {
        var workspacesDir = Path.Combine(workdir, "state", "workspaces");
        var workspaceRoot = Path.Combine(workspacesDir, appId);

        // Delete existing workspace (determinism)
        if (Directory.Exists(workspaceRoot))
            Directory.Delete(workspaceRoot, recursive: true);

        // Create fresh workspace structure
        Directory.CreateDirectory(workspaceRoot);
        var appsDir = Path.Combine(workspaceRoot, "apps");
        Directory.CreateDirectory(appsDir);
        var appDir = Path.Combine(appsDir, appId);
        Directory.CreateDirectory(appDir);

        return workspaceRoot;
    }
}
