namespace GovernorCli.Domain.Exceptions;

/// <summary>
/// Raised when repository layout validation fails.
/// </summary>
public class InvalidRepoLayoutException : Exception
{
    public List<string> ValidationProblems { get; }

    public InvalidRepoLayoutException(List<string> problems)
        : base($"Repository layout validation failed with {problems.Count} problem(s):\n" + 
                string.Join("\n", problems.Select(p => $"  - {p}")))
    {
        ValidationProblems = problems;
    }
}
