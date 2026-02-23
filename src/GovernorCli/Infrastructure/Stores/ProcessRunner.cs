using GovernorCli.Application.Stores;
using GovernorCli.Domain.Enums;
using GovernorCli.Domain.Exceptions;
using System.Diagnostics;
using System.Text;

namespace GovernorCli.Infrastructure.Stores;

/// <summary>
/// STRICT process runner: dotnet only, no shell, argument-safe.
/// Phase 3 MVP: Only dotnet build and dotnet run permitted.
/// </summary>
public class ProcessRunner : IProcessRunner
{
    /// <summary>
    /// Execute an allowlisted process with safe argument handling.
    /// Only dotnet build and dotnet run are permitted.
    /// NO shell invocation. NO arbitrary command execution.
    /// </summary>
    public int Run(AllowedProcess process, string workingDirectory, string[] args, string stdoutFile, string stderrFile)
    {
        // 1) Validate process is allowlisted
        var (allowedCommand, allowedArgs) = GetAllowedCommand(process);

        // 2) Validate args for injection (no special shell chars)
        ValidateArgs(args);

        // 3) Build ProcessStartInfo (NO shell)
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // 4) Use ArgumentList (safe argument passing, no shell parsing)
        psi.ArgumentList.Add(allowedCommand);
        foreach (var arg in allowedArgs)
        {
            psi.ArgumentList.Add(arg);
        }
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        // 5) Execute process
        using (var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process"))
        {
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();

            proc.WaitForExit();

            // 6) Write output files
            Directory.CreateDirectory(Path.GetDirectoryName(stdoutFile) ?? ".");
            Directory.CreateDirectory(Path.GetDirectoryName(stderrFile) ?? ".");

            File.WriteAllText(stdoutFile, stdout.Result);
            File.WriteAllText(stderrFile, stderr.Result);

            return proc.ExitCode;
        }
    }

    /// <summary>
    /// Map allowlisted process to dotnet subcommand.
    /// </summary>
    private static (string command, string[] args) GetAllowedCommand(AllowedProcess process)
    {
        return process switch
        {
            AllowedProcess.DotnetBuild => ("build", Array.Empty<string>()),
            AllowedProcess.DotnetRun => ("run", Array.Empty<string>()),
            _ => throw new ForbiddenProcessException($"Process not allowed: {process}. Only dotnet build and dotnet run are permitted in Phase 3.")
        };
    }

    /// <summary>
    /// Validate args do not contain shell injection vectors.
    /// Forbids: & | ; $ ` ( ) { } < > " ' * ? \
    /// </summary>
    private static void ValidateArgs(string[] args)
    {
        var forbiddenChars = new[] { '&', '|', ';', '$', '`', '(', ')', '{', '}', '<', '>', '"', '\'', '*', '?', '\\' };

        foreach (var arg in args)
        {
            foreach (var forbidden in forbiddenChars)
            {
                if (arg.Contains(forbidden))
                {
                    throw new ForbiddenProcessException($"Forbidden character '{forbidden}' in argument: {arg}");
                }
            }
        }
    }
}
