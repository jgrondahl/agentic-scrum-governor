using GovernorCli.State;

namespace GovernorCli.Application.Stores;

/// <summary>
/// Abstraction for backlog persistence.
/// Responsible for loading and saving backlog state.
/// </summary>
public interface IBacklogStore
{
    /// <summary>
    /// Load entire backlog from YAML file.
    /// </summary>
    /// <param name="filePath">Path to backlog.yaml</param>
    /// <returns>Deserialized BacklogFile</returns>
    /// <exception cref="BacklogParseException">If file cannot be parsed</exception>
    BacklogFile Load(string filePath);

    /// <summary>
    /// Persist backlog to YAML file.
    /// Best-effort write: File.WriteAllText semantics.
    /// Not atomic under crash conditions.
    /// </summary>
    /// <param name="filePath">Path to backlog.yaml</param>
    /// <param name="backlog">BacklogFile to persist</param>
    void Save(string filePath, BacklogFile backlog);
}
