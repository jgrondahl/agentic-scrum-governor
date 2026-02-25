namespace GovernorCli.Application.Stores;

/// <summary>
/// Service to compute patch preview: differences between candidate and approved implementation plans.
/// Produces machine-readable patch.preview.json and human-readable patch.preview.diff files.
/// </summary>
public interface IPatchPreviewService
{
    /// <summary>
    /// Compute patch preview for file changes between a candidate plan (in run folder)
    /// and the current approved plan (if exists in state/plans/).
    /// </summary>
    /// <param name="workdir">Repository root</param>
    /// <param name="itemId">Backlog item ID</param>
    /// <param name="candidatePlanPath">Path to candidate implementation.plan.json in run folder</param>
    /// <returns>Typed PatchPreview object (never null)</returns>
    PatchPreviewData ComputePatchPreview(string workdir, int itemId, string candidatePlanPath);

    /// <summary>
    /// Format patch preview as diff lines (one per file changed).
    /// Format: ACTION path (e.g., "A apps/myapp/Program.cs", "M apps/myapp/myapp.csproj")
    /// Paths are repo-relative using / separators.
    /// </summary>
    /// <param name="preview">Patch preview data</param>
    /// <returns>List of diff lines</returns>
    List<string> FormatDiffLines(PatchPreviewData preview);
}

/// <summary>
/// Typed patch preview data: what files would change if approved.
/// </summary>
public sealed class PatchPreviewData
{
    public required string ComputedAtUtc { get; set; }
    public required int ItemId { get; set; }
    public required List<PatchFileChange> Changes { get; set; }
}

/// <summary>
/// Single file change in a patch: add, modify, or delete.
/// </summary>
public sealed class PatchFileChange
{
    /// <summary>
    /// Action: "A" (add), "M" (modify), "D" (delete)
    /// </summary>
    public required string Action { get; set; }

    /// <summary>
    /// Repo-relative path (using / separators, normalized from Windows \)
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// Size of candidate file in bytes
    /// </summary>
    public long CandidateSizeBytes { get; set; }

    /// <summary>
    /// SHA256 hash of candidate file (hex string)
    /// </summary>
    public string CandidateSha256 { get; set; } = "";

    /// <summary>
    /// Size of current/approved file in bytes (0 if doesn't exist)
    /// </summary>
    public long RepoSizeBytes { get; set; }

    /// <summary>
    /// SHA256 hash of current/approved file (empty if doesn't exist)
    /// </summary>
    public string RepoSha256 { get; set; } = "";
}
