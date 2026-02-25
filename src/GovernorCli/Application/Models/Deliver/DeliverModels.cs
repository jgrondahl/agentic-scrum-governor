using System.Text.Json.Serialization;

namespace GovernorCli.Application.Models.Deliver;

/// <summary>
/// Represents the implementation plan for a delivery run.
/// Deterministic artifact describing what will be generated.
/// </summary>
public class DeliveryImplementationPlan
{
    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = "";

    [JsonPropertyName("item_id")]
    public int ItemId { get; set; }

    [JsonPropertyName("app_id")]
    public string AppId { get; set; } = "";

    [JsonPropertyName("template_id")]
    public string TemplateId { get; set; } = "";

    [JsonPropertyName("workspace_target_path")]
    public string WorkspaceTargetPath { get; set; } = "";

    [JsonPropertyName("actions")]
    public List<string> Actions { get; set; } = new();
}

/// <summary>
/// Result of a single validation command execution.
/// Captures command invocation and output for audit trail.
/// </summary>
public class ValidationCommandResult
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("working_dir")]
    public string WorkingDir { get; set; } = "";

    [JsonPropertyName("command_line")]
    public string CommandLine { get; set; } = "";

    [JsonPropertyName("exit_code")]
    public int ExitCode { get; set; }

    [JsonPropertyName("stdout_file")]
    public string StdoutFile { get; set; } = "";

    [JsonPropertyName("stderr_file")]
    public string StderrFile { get; set; } = "";
}

/// <summary>
/// Complete validation report for the candidate implementation.
/// All commands must exit 0 for validation to pass.
/// </summary>
public class ValidationReport
{
    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("commands")]
    public List<ValidationCommandResult> Commands { get; set; } = new();
}

/// <summary>
/// Single file change in a patch.
/// Typed representation with action, path, size, and cryptographic hash.
/// Diff format: ACTION|path|size|sha256
/// Example: A|hello-world-app/Program.cs|245|abc123def456...
/// </summary>
public class PatchFile
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";  // "A" (add), "M" (modify), "D" (delete)

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("workspace_sha256")]
    public string WorkspaceSha256 { get; set; } = "";

    [JsonPropertyName("repo_sha256")]
    public string? RepoSha256 { get; set; }  // null for add, present for modify/delete
}

/// <summary>
/// Patch preview describing changes that would be applied to /apps/<appId>/.
/// Read-only artifact; does not modify repository.
/// All files are typed with action, path, size, hash for defensible auditability.
/// </summary>
public class DeliverPatchPreview
{
    [JsonPropertyName("computed_at_utc")]
    public string ComputedAtUtc { get; set; } = "";

    [JsonPropertyName("item_id")]
    public int ItemId { get; set; }

    [JsonPropertyName("app_id")]
    public string AppId { get; set; } = "";

    [JsonPropertyName("repo_target")]
    public string RepoTarget { get; set; } = "";

    [JsonPropertyName("files")]
    public List<PatchFile> Files { get; set; } = new();  // Typed, not string list

    [JsonPropertyName("validation_passed")]
    public bool ValidationPassed { get; set; }
}

/// <summary>
/// Authoritative record of applied patch.
/// Written only after --approve with successful validation.
/// Appends to decision log immutably.
/// </summary>
public class PatchApplied
{
    [JsonPropertyName("applied_at_utc")]
    public string AppliedAtUtc { get; set; } = "";

    [JsonPropertyName("item_id")]
    public int ItemId { get; set; }

    [JsonPropertyName("app_id")]
    public string AppId { get; set; } = "";

    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = "";

    [JsonPropertyName("repo_target")]
    public string RepoTarget { get; set; } = "";

    [JsonPropertyName("files_applied")]
    public List<PatchFile> FilesApplied { get; set; } = new();  // Typed, not string list
}

/// <summary>
/// Complete result of a deliver operation.
/// Contains all artifacts and paths for reference.
/// Passed to Flow for orchestration decisions.
/// </summary>
public class DeliverResult
{
    public DeliveryImplementationPlan Plan { get; set; } = new();
    public ValidationReport Validation { get; set; } = new();
    public DeliverPatchPreview Preview { get; set; } = new();
    public PatchApplied? PatchApplied { get; set; }

    public string WorkspaceRoot { get; set; } = "";
    public string WorkspaceAppRoot { get; set; } = "";
    public string RunDir { get; set; } = "";
}
