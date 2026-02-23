using GovernorCli.Domain.Enums;

namespace GovernorCli.Application.Stores;

/// <summary>
/// Abstraction for process execution with output capture.
/// RESTRICTED: Only allowlisted processes can be executed (dotnet build, dotnet run).
/// Prevents arbitrary command injection and enforces governance.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Execute an allowlisted process and capture output.
    /// Only dotnet build and dotnet run are permitted in Phase 3 MVP.
    /// </summary>
    /// <param name="process">Allowlisted process (dotnet build or dotnet run)</param>
    /// <param name="workingDirectory">Working directory for process</param>
    /// <param name="args">Process arguments (no shell commands allowed)</param>
    /// <param name="stdoutFile">Path to write stdout</param>
    /// <param name="stderrFile">Path to write stderr</param>
    /// <returns>Exit code from process</returns>
    /// <exception cref="ArgumentException">If args contain shell commands or forbidden tokens</exception>
    int Run(AllowedProcess process, string workingDirectory, string[] args, string stdoutFile, string stderrFile);
}
