using System.Text.Json.Serialization;

namespace GovernorCli.Application.Models;

/// <summary>
/// Typed implementation plan for technical refinement phase (Phase 2).
/// Deterministic, machine-readable plan describing app structure, build/run procedures,
/// validation checks, and deployment constraints.
/// 
/// Written to: state/runs/{runId}/implementation.plan.json (candidate)
/// Persisted to: state/plans/item-{itemId}/implementation.plan.json (approved)
/// </summary>
public sealed class ImplementationPlan
{
    [JsonPropertyName("plan_id")]
    public string PlanId { get; set; } = "";

    [JsonPropertyName("created_at_utc")]
    public string CreatedAtUtc { get; set; } = "";

    [JsonPropertyName("created_from_run_id")]
    public string CreatedFromRunId { get; set; } = "";

    [JsonPropertyName("item_id")]
    public int ItemId { get; set; }

    [JsonPropertyName("epic_id")]
    public string EpicId { get; set; } = "";

    [JsonPropertyName("app_id")]
    public string AppId { get; set; } = "";

    [JsonPropertyName("repo_target")]
    public string RepoTarget { get; set; } = "";

    [JsonPropertyName("app_type")]
    public string AppType { get; set; } = "dotnet_console";

    [JsonPropertyName("stack")]
    public StackInfo Stack { get; set; } = new();

    [JsonPropertyName("project_layout")]
    public List<ProjectFile> ProjectLayout { get; set; } = new();

    [JsonPropertyName("build_plan")]
    public List<ExecutionStep> BuildPlan { get; set; } = new();

    [JsonPropertyName("run_plan")]
    public List<ExecutionStep> RunPlan { get; set; } = new();

    [JsonPropertyName("validation_checks")]
    public List<ValidationCheck> ValidationChecks { get; set; } = new();

    [JsonPropertyName("patch_policy")]
    public PatchPolicy PatchPolicy { get; set; } = new();

    [JsonPropertyName("risks")]
    public List<string> Risks { get; set; } = new();

    [JsonPropertyName("assumptions")]
    public List<string> Assumptions { get; set; } = new();

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("code_generation")]
    public CodeGenerationSpec? CodeGeneration { get; set; }

    [JsonPropertyName("generation_phase")]
    public string GenerationPhase { get; set; } = "pending";
}

/// <summary>
/// Technology stack definition: language, runtime, framework.
/// </summary>
public sealed class StackInfo
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "csharp";

    [JsonPropertyName("runtime")]
    public string Runtime { get; set; } = "net8.0";

    [JsonPropertyName("framework")]
    public string Framework { get; set; } = "dotnet";
}

/// <summary>
/// Project file or directory in the application layout.
/// </summary>
public sealed class ProjectFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = ""; // "source", "project", "config", "test", etc.
}

/// <summary>
/// Single step in a build or run plan.
/// Represents: tool invocation (dotnet, npm, make, etc.)
/// </summary>
public sealed class ExecutionStep
{
    [JsonPropertyName("tool")]
    public string Tool { get; set; } = "";

    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = new();

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = ".";
}

/// <summary>
/// Validation check: what must be true for successful execution.
/// Type: exit_code_equals, exit_code_zero, stdout_contains, stdout_equals, etc.
/// </summary>
public sealed class ValidationCheck
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

/// <summary>
/// Patch exclusion policy: patterns (globs) to exclude when computing diffs.
/// </summary>
public sealed class PatchPolicy
{
    [JsonPropertyName("exclude_globs")]
    public List<string> ExcludeGlobs { get; set; } = new();
}

/// <summary>
/// Code generation specification for Phase 3 (Deliver).
/// </summary>
public sealed class CodeGenerationSpec
{
    [JsonPropertyName("source_files")]
    public List<SourceFileSpec> SourceFiles { get; set; } = new();

    [JsonPropertyName("test_files")]
    public List<SourceFileSpec> TestFiles { get; set; } = new();

    [JsonPropertyName("config_files")]
    public List<SourceFileSpec> ConfigFiles { get; set; } = new();

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = "";

    [JsonPropertyName("generated_at_utc")]
    public string GeneratedAtUtc { get; set; } = "";
}

/// <summary>
/// Individual file to be generated.
/// </summary>
public sealed class SourceFileSpec
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";
}
