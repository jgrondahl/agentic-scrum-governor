namespace GovernorCli.Application.Stores;

/// <summary>
/// Abstraction for run artifact storage.
/// Responsible for managing run directories and writing artifacts.
/// </summary>
public interface IRunArtifactStore
{
    /// <summary>
    /// Create run directory under state/runs/{runId}.
    /// Idempotent: safe to call multiple times for same runId.
    /// </summary>
    /// <param name="baseDir">Base directory (typically workdir/state/runs)</param>
    /// <param name="runId">Run identifier (yyyyMMdd_HHmmss_flow_item-X)</param>
    /// <returns>Absolute path to created run directory</returns>
    string CreateRunFolder(string baseDir, string runId);

    /// <summary>
    /// Write JSON artifact to run directory.
    /// Best-effort write: File.WriteAllText semantics.
    /// Overwrites silently if file exists.
    /// Not atomic under crash conditions.
    /// Future: May be hardened to atomic write (temp file + move).
    /// </summary>
    /// <param name="runDir">Run directory path</param>
    /// <param name="fileName">File name (e.g., "estimation.json")</param>
    /// <param name="payload">Object to serialize (will be pretty-printed)</param>
    void WriteJson(string runDir, string fileName, object payload);

    /// <summary>
    /// Write text artifact to run directory.
    /// Best-effort write: File.WriteAllText semantics.
    /// Overwrites silently if file exists.
    /// </summary>
    /// <param name="runDir">Run directory path</param>
    /// <param name="fileName">File name (e.g., "summary.md")</param>
    /// <param name="content">Text content</param>
    void WriteText(string runDir, string fileName, string content);
}
