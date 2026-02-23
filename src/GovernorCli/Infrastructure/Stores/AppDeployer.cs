using GovernorCli.Application.Models.Deliver;
using GovernorCli.Application.Stores;

namespace GovernorCli.Infrastructure.Stores;

public class AppDeployer : IAppDeployer
{
    public List<PatchFile> Deploy(string workdir, string workspaceRoot, string appId)
    {
        var sourceDir = Path.Combine(workspaceRoot, "apps", appId);
        var targetDir = Path.Combine(workdir, "apps", appId);

        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Workspace app directory not found: {sourceDir}");

        // Create target directory
        Directory.CreateDirectory(Path.GetDirectoryName(targetDir) ?? ".");
        if (Directory.Exists(targetDir))
            Directory.Delete(targetDir, recursive: true);

        Directory.CreateDirectory(targetDir);

        // Copy all files recursively and track them as typed PatchFile records
        var deployed = new List<PatchFile>();
        CopyDirectory(sourceDir, targetDir, deployed, appId);

        return deployed;
    }

    private static void CopyDirectory(string sourceDir, string targetDir, List<PatchFile> deployed, string appId)
    {
        var di = new DirectoryInfo(sourceDir);
        foreach (var file in di.GetFiles())
        {
            var targetFile = Path.Combine(targetDir, file.Name);
            file.CopyTo(targetFile, overwrite: true);
            var relativePath = Path.Combine(appId, file.Name);
            var sha256 = ComputeFileSha256(targetFile);

            deployed.Add(new PatchFile
            {
                Action = "A",
                Path = relativePath,
                Size = file.Length,
                WorkspaceSha256 = ComputeFileSha256(file.FullName),
                RepoSha256 = sha256
            });
        }

        foreach (var subDir in di.GetDirectories())
        {
            var targetSubDir = Path.Combine(targetDir, subDir.Name);
            Directory.CreateDirectory(targetSubDir);
            CopyDirectory(subDir.FullName, targetSubDir, deployed, Path.Combine(appId, subDir.Name));
        }
    }

    private static string ComputeFileSha256(string filePath)
    {
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            using (var stream = File.OpenRead(filePath))
            {
                var hash = sha256.ComputeHash(stream);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
        }
    }
}
