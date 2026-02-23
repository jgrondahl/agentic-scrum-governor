namespace GovernorCli.Domain.Enums;

/// <summary>
/// Allowlisted processes that can be executed by the Deliver Engine.
/// Restricted for security and governance.
/// </summary>
public enum AllowedProcess
{
    /// <summary>
    /// dotnet CLI (build, run only; no new, publish, etc.)
    /// </summary>
    DotnetBuild,
    
    /// <summary>
    /// dotnet CLI (run executable)
    /// </summary>
    DotnetRun
}
