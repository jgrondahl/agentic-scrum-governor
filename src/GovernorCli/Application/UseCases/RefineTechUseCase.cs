using GovernorCli.Application.Models;
using GovernorCli.Application.Stores;
using GovernorCli.Domain.Exceptions;
using GovernorCli.LanguageModel;
using GovernorCli.Personas;
using GovernorCli.State;
using System.Text.Json;

namespace GovernorCli.Application.UseCases;

/// <summary>
/// Request for technical readiness review.
/// All decision context comes from Flow (approver, runId, paths).
/// UseCase does not read environment or format decisions.
/// </summary>
public class RefineTechRequest
{
    public int ItemId { get; set; }
    public string BacklogPath { get; set; } = "";
    public string RunsDir { get; set; } = "";
    public string Workdir { get; set; } = "";
    public string RunId { get; set; } = "";
    public bool Approve { get; set; }
    
    // LLM model configuration
    public bool UseSameModel { get; set; }
    public string? ModelSad { get; set; }
    public string? ModelSasd { get; set; }
    public string? ModelQa { get; set; }
    
    public PersonaModelConfig GetModelConfig()
    {
        var config = PersonaModelConfig.FromEnvironment();
        
        if (UseSameModel)
        {
            var model = ModelSad ?? ModelSasd ?? ModelQa ?? config.DefaultModel;
            return config.WithSameModel(model);
        }
        
        if (!string.IsNullOrEmpty(ModelSad))
            config = config.WithOverride(PersonaId.SAD, ModelSad);
        if (!string.IsNullOrEmpty(ModelSasd))
            config = config.WithOverride(PersonaId.SASD, ModelSasd);
        if (!string.IsNullOrEmpty(ModelQa))
            config = config.WithOverride(PersonaId.QA, ModelQa);
        
        return config;
    }
}

public class RefineTechResult
{
    public bool Success { get; set; }
    public string RunId { get; set; } = "";
    public PatchPreview? Patch { get; set; }
}

/// <summary>
/// Business logic for technical readiness review (TRR).
/// Responsibility: Compute estimates, generate implementation plans, generate artifacts, optionally persist.
/// Zero responsibility: File I/O (via stores), orchestration (via Flow).
/// </summary>
public class RefineTechUseCase
{
    private static readonly int[] ValidFibonacci = { 1, 3, 5 };
    private static readonly PersonaId[] EstimationTeam = [PersonaId.SAD, PersonaId.SASD, PersonaId.QA];

    private readonly IBacklogStore _backlogStore;
    private readonly IRunArtifactStore _runArtifactStore;
    private readonly IEpicStore _epicStore;
    private readonly IPlanStore _planStore;
    private readonly IPatchPreviewService _patchPreviewService;

    public RefineTechUseCase(
        IBacklogStore backlogStore,
        IRunArtifactStore runArtifactStore,
        IEpicStore epicStore,
        IPlanStore planStore,
        IPatchPreviewService patchPreviewService)
    {
        _backlogStore = backlogStore;
        _runArtifactStore = runArtifactStore;
        _epicStore = epicStore;
        _planStore = planStore;
        _patchPreviewService = patchPreviewService;
    }

    /// <summary>
    /// Process technical readiness review.
    /// Phase 2 MVP: Generate candidate implementation plan + patch preview.
    /// On approval: Persist plan to state/plans/, update backlog with reference.
    /// Returns typed RefineTechResult for all cases.
    /// </summary>
    public RefineTechResult Process(RefineTechRequest request)
    {
        var utc = DateTimeOffset.UtcNow;

        // Load backlog (via store, abstract)
        var backlog = _backlogStore.Load(request.BacklogPath);

        // Validate item exists
        var item = backlog.Backlog.FirstOrDefault(x => x.Id == request.ItemId)
            ?? throw new ItemNotFoundException(request.ItemId);

        // Validate epic_id is present (Phase 2 precondition)
        if (string.IsNullOrWhiteSpace(item.EpicId))
        {
            WriteFailureSummary(request, "EpicIdMissing",
                "Item requires epic_id for technical refinement. Set epic_id in backlog and try again.");
            throw new InvalidOperationException($"Item {request.ItemId} missing epic_id");
        }

        // Resolve epic_id -> app_id
        string appId;
        try
        {
            appId = _epicStore.ResolveAppId(request.Workdir, item.EpicId);
        }
        catch (Exception ex)
        {
            WriteFailureSummary(request, "EpicResolutionFailed",
                $"Could not resolve epic_id '{item.EpicId}' to app_id. Ensure state/epics.yaml exists and contains this epic. Error: {ex.Message}");
            throw;
        }

        // Create run directory
        var runDir = _runArtifactStore.CreateRunFolder(request.RunsDir, request.RunId);

        // 1) Run estimation consensus (SAD, SASD, QA vote on Fibonacci)
        var estimate = RunEstimationConsensus(request, item, runDir);
        _runArtifactStore.WriteJson(runDir, "estimation.json", estimate);

        // Validate: app_type is required for implementation plan
        if (string.IsNullOrEmpty(estimate.AppType))
        {
            WriteFailureSummary(request, "AppTypeMissing",
                "app_type is missing and requires an architectural decision. " +
                "Please re-run the command and ensure the LLM provides an app_type in the estimation response. " +
                "Examples: web_blazor, web_api, console, library");
            throw new InvalidOperationException(
                "app_type is missing and requires an architectural decision. " +
                "Please re-run the command or specify app_type manually.");
        }

        // 2) Generate real design artifacts via LLM (resumable)
        var modelConfig = request.GetModelConfig();
        
        var architecture = GenerateArchitectureDesign(request, item, modelConfig, runDir);
        _runArtifactStore.WriteText(runDir, "architecture.md", architecture);

        var qaPlan = GenerateQaPlan(request, item, modelConfig, runDir);
        _runArtifactStore.WriteText(runDir, "qa-plan.md", qaPlan);

        var technicalTasks = GenerateTechnicalTasks(request, item, estimate, modelConfig, runDir);
        _runArtifactStore.WriteText(runDir, "technical-tasks.yaml", technicalTasks);

        // 3) Generate candidate implementation plan (typed, deterministic)
        var plan = BuildImplementationPlan(
            request.RunId, request.ItemId, item, appId, estimate, utc);
        var candidatePlanPath = Path.Combine(runDir, "implementation.plan.json");
        _runArtifactStore.WriteJson(runDir, "implementation.plan.json", plan);

        // 3) Compute patch preview (read-only, compare to approved if exists)
        var patchPreview = _patchPreviewService.ComputePatchPreview(
            request.Workdir, request.ItemId, candidatePlanPath);
        _runArtifactStore.WriteJson(runDir, "patch.preview.json", patchPreview);

        // 4) Write patch preview diff (human-readable)
        var diffLines = _patchPreviewService.FormatDiffLines(patchPreview);
        var diffText = string.Join("\n", diffLines);
        _runArtifactStore.WriteText(runDir, "patch.preview.diff", diffText);

        // 5) Compute backlog patch preview (existing behavior)
        var backlogPatch = ComputePatchPreview(item, estimate, request.RunId, utc);
        _runArtifactStore.WriteJson(runDir, "patch.backlog.json", backlogPatch);

        // 6) If not approved, done
        if (!request.Approve)
        {
            _runArtifactStore.WriteText(runDir, "summary.md",
                $"# Refine-Tech Summary\n\n" +
                $"✓ Preview generated for item {request.ItemId}.\n\n" +
                $"Outputs:\n" +
                $"- implementation.plan.json (candidate)\n" +
                $"- patch.preview.json (file changes)\n" +
                $"- patch.preview.diff (diff lines)\n" +
                $"- estimation.json, architecture.md, qa-plan.md, technical-tasks.yaml\n\n" +
                $"Next: Review above and approve with:\n" +
                $"  governor refine-tech --item {request.ItemId} --approve\n");

            return new RefineTechResult
            {
                Success = true,
                RunId = request.RunId,
                Patch = backlogPatch
            };
        }

        // 7) APPROVAL PATH: Persist implementation plan
        ValidateAndPersistPlan(request.Workdir, request.ItemId, plan, estimate, runDir);

        // 8) Apply backlog patch
        ApplyPatchAndPersist(request.BacklogPath, backlog, request.ItemId, estimate,
            request.RunId, _planStore.GetPlanPath(request.Workdir, request.ItemId));

        // 9) Write applied patch record
        var appliedPatch = new AppliedPatch
        {
            AppliedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            ItemId = request.ItemId,
            RunId = request.RunId,
            Changes = backlogPatch.Changes
        };

        _runArtifactStore.WriteJson(runDir, "patch.backlog.applied.json", appliedPatch);

        // 10) Write success summary
        _runArtifactStore.WriteText(runDir, "summary.md",
            $"# Refine-Tech Summary\n\n" +
            $"✓ Approved and applied for item {request.ItemId}.\n\n" +
            $"Changes:\n" +
            $"- Implementation plan persisted to: state/plans/item-{request.ItemId}/implementation.plan.json\n" +
            $"- Backlog updated:\n" +
            $"  - Status → ready_for_dev\n" +
            $"  - Estimate embedded\n" +
            $"  - implementation_plan_ref → state/plans/item-{request.ItemId}/implementation.plan.json\n" +
            $"  - Technical notes ref → runs/{request.RunId}/\n\n" +
            $"Decision log appended (by Flow).\n");

        return new RefineTechResult
        {
            Success = true,
            RunId = request.RunId,
            Patch = backlogPatch
        };
    }

    // --- Helper methods (business logic only, no I/O) ---

    private PatchPreview ComputePatchPreview(
        BacklogItem original,
        EstimateModel estimate,
        string runId,
        DateTimeOffset utc)
    {
        return new PatchPreview
        {
            ComputedAtUtc = utc.ToString("O"),
            ItemId = original.Id,
            Changes = new PatchChanges
            {
                Status = new StatusChange
                {
                    Before = original.Status ?? "candidate",
                    After = "ready_for_dev"
                },
                Estimate = new EstimateChange
                {
                    Before = original.Estimate,
                    After = new BacklogEstimate
                    {
                        Id = estimate.EstimateId,
                        StoryPoints = estimate.StoryPoints,
                        Scale = estimate.Scale,
                        Confidence = estimate.Confidence,
                        RiskLevel = estimate.RiskLevel,
                        ComplexityDrivers = estimate.ComplexityDrivers,
                        Assumptions = estimate.Assumptions,
                        Dependencies = estimate.Dependencies,
                        NonGoals = estimate.NonGoals,
                        Notes = estimate.Notes,
                        CreatedAtUtc = estimate.CreatedAtUtc,
                        CreatedFromRunId = estimate.CreatedFromRunId
                    }
                },
                TechnicalNotesRef = new RefChange
                {
                    Before = original.TechnicalNotesRef,
                    After = $"runs/{runId}/"
                }
            }
        };
    }

    private void ValidateAndPersistPlan(
        string workdir, 
        int itemId, 
        ImplementationPlan plan, 
        EstimateModel estimate,
        string? runDir = null)
    {
        // Minimal validations
        if (string.IsNullOrWhiteSpace(plan.PlanId))
            throw new InvalidOperationException("Plan must have plan_id set");

        // Fibonacci validation: story points must be 1, 3, or 5
        if (!ValidFibonacci.Contains(estimate.StoryPoints))
            throw new InvalidOperationException(
                $"Story points {estimate.StoryPoints} is not valid. Must be 1, 3, or 5. " +
                "Break down into smaller items and resubmit.");

        if (string.IsNullOrWhiteSpace(plan.Notes) || plan.Notes.Contains("Placeholder"))
            throw new InvalidOperationException("Plan notes cannot be empty or contain placeholder text");

        // Validate design artifacts if runDir is provided
        if (runDir != null)
        {
            ValidateArtifactContent(Path.Combine(runDir, "architecture.md"), "Architecture");
            ValidateArtifactContent(Path.Combine(runDir, "qa-plan.md"), "QA Plan");
            ValidateArtifactContent(Path.Combine(runDir, "technical-tasks.yaml"), "Technical Tasks");
        }

        // Persist to approved location
        _planStore.SavePlan(workdir, itemId, plan);
    }

    private void ValidateArtifactContent(string artifactPath, string artifactName)
    {
        if (!File.Exists(artifactPath))
            throw new InvalidOperationException($"{artifactName} is missing. Run refine-tech without --approve first.");

        var content = File.ReadAllText(artifactPath);
        
        if (content.Contains("(fill)") || content.Contains("Placeholder"))
            throw new InvalidOperationException($"{artifactName} contains placeholder content. Complete the design before approval.");

        if (content.Length < 200)
            throw new InvalidOperationException($"{artifactName} is too short. Provide complete design details.");
    }

    private void ApplyPatchAndPersist(
        string backlogPath,
        BacklogFile backlog,
        int itemId,
        EstimateModel estimate,
        string runId,
        string implementationPlanRef)
    {
        var item = backlog.Backlog.First(x => x.Id == itemId);

        item.Status = "ready_for_dev";
        item.TechnicalNotesRef = $"runs/{runId}/";
        item.ImplementationPlanRef = implementationPlanRef;
        item.Estimate = new BacklogEstimate
        {
            Id = estimate.EstimateId,
            StoryPoints = estimate.StoryPoints,
            Scale = estimate.Scale,
            Confidence = estimate.Confidence,
            RiskLevel = estimate.RiskLevel,
            ComplexityDrivers = estimate.ComplexityDrivers,
            Assumptions = estimate.Assumptions,
            Dependencies = estimate.Dependencies,
            NonGoals = estimate.NonGoals,
            Notes = estimate.Notes,
            CreatedAtUtc = estimate.CreatedAtUtc,
            CreatedFromRunId = estimate.CreatedFromRunId
        };

        _backlogStore.Save(backlogPath, backlog);
    }

    private void WriteFailureSummary(RefineTechRequest request, string reason, string details)
    {
        var runDir = _runArtifactStore.CreateRunFolder(request.RunsDir, request.RunId);
        _runArtifactStore.WriteText(runDir, "summary.md",
            $"# Refine-Tech Summary\n\n" +
            $"✗ FAILED\n\n" +
            $"**Reason:** {reason}\n\n" +
            $"**Details:**\n\n" +
            $"{details}\n");
    }

    private ImplementationPlan BuildImplementationPlan(
        string runId,
        int itemId,
        BacklogItem item,
        string appId,
        EstimateModel estimate,
        DateTimeOffset utc)
    {
        // Deterministic plan ID based on run and item
        var planId = $"PLAN-{runId.Replace("_", "-").Substring(0, Math.Min(30, runId.Length))}";

        // Use LLM-determined architecture values
        var repoTarget = $"apps/{appId}";
        var appType = estimate.AppType ?? "dotnet_console";
        var language = estimate.Language ?? "csharp";
        var runtime = estimate.Runtime ?? "net8.0";
        var framework = estimate.Framework ?? "console";

        // Build project layout from LLM-provided projects or use default
        var projectLayout = estimate.Projects?.Count > 0
            ? estimate.Projects.Select(p => new ProjectFile
            {
                Path = p.Path,
                Kind = p.Type
            }).ToList()
            : new List<ProjectFile>
            {
                new() { Path = "Program.cs", Kind = "source" },
                new() { Path = $"{appId}.csproj", Kind = "project" },
                new() { Path = ".gitignore", Kind = "config" },
                new() { Path = "README.md", Kind = "docs" }
            };

        return new ImplementationPlan
        {
            PlanId = planId,
            CreatedAtUtc = utc.ToString("O"),
            CreatedFromRunId = runId,
            ItemId = itemId,
            EpicId = item.EpicId ?? "",
            AppId = appId,
            RepoTarget = repoTarget,
            AppType = appType,
            Stack = new StackInfo
            {
                Language = language,
                Runtime = runtime,
                Framework = framework
            },
            ProjectLayout = projectLayout,
            BuildPlan = new List<ExecutionStep>
            {
                new()
                {
                    Tool = "dotnet",
                    Args = new List<string> { "build", "-c", "Release" },
                    Cwd = "."
                }
            },
            RunPlan = new List<ExecutionStep>
            {
                new()
                {
                    Tool = "dotnet",
                    Args = new List<string> { "run", "-c", "Release", "--no-build" },
                    Cwd = "."
                }
            },
            ValidationChecks = new List<ValidationCheck>
            {
                new()
                {
                    Type = "exit_code_equals",
                    Value = "0"
                }
            },
            PatchPolicy = new PatchPolicy
            {
                ExcludeGlobs = new List<string>
                {
                    "bin/**",
                    "obj/**",
                    ".vs/**",
                    "**/*.user",
                    "**/*.suo"
                }
            },
            Risks = item.Risks ?? new List<string>(),
            Assumptions = estimate.Assumptions ?? new List<string>(),
            Notes = $"Implementation plan for item {itemId} ({item.Title}). Generated from refine-tech run {runId}."
        };
    }

    private EstimateModel BuildPlaceholderEstimate(string runId, int itemId, DateTimeOffset utc)
    {
        return new EstimateModel
        {
            EstimateId = $"EST-{runId}-{itemId}",
            StoryPoints = 5,
            Scale = "fibonacci",
            Confidence = "medium",
            RiskLevel = "medium",
            ComplexityDrivers = new List<string> { "Placeholder: replace with real drivers during TRR." },
            Assumptions = new List<string> { "Placeholder: list assumptions during TRR." },
            Dependencies = new List<string>(),
            NonGoals = new List<string>(),
            Notes = "Placeholder estimate produced by refine-tech skeleton.",
            CreatedAtUtc = utc.ToString("O"),
            CreatedFromRunId = runId
        };
    }

    private string BuildArchitectureTemplate(BacklogItem item)
    {
        return $"""
        # Architecture Overview

        ## System Boundaries
        - Touches: (fill)
        - Does NOT touch: (fill)

        ## Data Flow
        - Inputs:
        - Processing stages:
        - Outputs:

        ## Key Design Decisions
        - (fill)

        ## Constraints
        - Performance:
        - Platform:
        - Libraries/dependencies:

        ## Deferred Decisions
        - (fill)

        ---
        Backlog Item: {item.Id} — {item.Title}
        """;
    }

    private string BuildQaPlanTemplate(BacklogItem item)
    {
        return $"""
        # QA Plan

        ## Validation Strategy
        - (fill)

        ## Test Types
        - Unit:
        - Integration:
        - Manual:

        ## Edge Cases
        - (fill)

        ## Definition of Done
        - (fill)

        ---
        Backlog Item: {item.Id} — {item.Title}
        """;
    }

    private string BuildTasksTemplate(string runId, int itemId, string estimateId)
    {
        return $"""
        estimateId: "{estimateId}"
        backlogItemId: {itemId}
        createdFromRunId: "{runId}"
        tasks:
          - id: T1
            title: "Placeholder task"
            owner: architect
            description: "Replace with actionable engineering task(s) during TRR."
            depends_on: []
            done_when:
              - "Definition of Done written"
        """;
    }

    private string GenerateArchitectureDesign(
        RefineTechRequest request,
        BacklogItem item,
        PersonaModelConfig modelConfig,
        string runDir)
    {
        var artifactPath = Path.Combine(runDir, "architecture.md");
        
        if (File.Exists(artifactPath))
        {
            return File.ReadAllText(artifactPath);
        }

        var flowPrompt = LoadFlowPrompt(request.Workdir, "refine-tech.md");
        var personaPrompt = LoadPersonaPrompt(request.Workdir, PersonaId.SAD, "sad-architecture.md");
        
        var context = $"""
        BACKLOG ITEM:
        - ID: {item.Id}
        - Title: {item.Title}
        - Story: {item.Story}
        
        Acceptance Criteria:
        {(item.AcceptanceCriteria.Count == 0 ? "(none)" : string.Join("\n", item.AcceptanceCriteria.Select((c, i) => $"{i + 1}. {c}")))}
        
        Non-Goals:
        {(item.NonGoals.Count == 0 ? "(none)" : string.Join("\n", item.NonGoals.Select((n, i) => $"{i + 1}. {n}")))}
        
        Dependencies:
        {(item.Dependencies.Count == 0 ? "(none)" : string.Join("\n", item.Dependencies.Select((d, i) => $"{i + 1}. {d}")))}
        
        Risks:
        {(item.Risks.Count == 0 ? "(none)" : string.Join("\n", item.Risks.Select((r, i) => $"{i + 1}. {r}")))}
        
        ---
        
        Generate the complete architecture document. 
        IMPORTANT: NO PLACEHOLDER CONTENT. Every section must have specific, concrete technical details.
        """;

        var provider = PersonaLlmProviderFactory.Create(PersonaId.SAD, modelConfig);
        
        var lmRequest = new LanguageModelRequest(
            provider.PersonaId,
            personaPrompt,
            flowPrompt,
            context);

        var response = provider.GenerateAsync(lmRequest, CancellationToken.None).GetAwaiter().GetResult();
        
        var architecture = CleanMarkdownOutput(response.OutputText);
        
        if (!Directory.Exists(runDir))
            Directory.CreateDirectory(runDir);
        
        File.WriteAllText(artifactPath, architecture);
        
        return architecture;
    }

    private string GenerateQaPlan(
        RefineTechRequest request,
        BacklogItem item,
        PersonaModelConfig modelConfig,
        string runDir)
    {
        var artifactPath = Path.Combine(runDir, "qa-plan.md");
        
        if (File.Exists(artifactPath))
        {
            return File.ReadAllText(artifactPath);
        }

        var flowPrompt = LoadFlowPrompt(request.Workdir, "refine-tech.md");
        var personaPrompt = LoadPersonaPrompt(request.Workdir, PersonaId.QA, "qa-qaplan.md");
        
        var context = $"""
        BACKLOG ITEM:
        - ID: {item.Id}
        - Title: {item.Title}
        - Story: {item.Story}
        
        Acceptance Criteria:
        {(item.AcceptanceCriteria.Count == 0 ? "(none)" : string.Join("\n", item.AcceptanceCriteria.Select((c, i) => $"{i + 1}. {c}")))}
        
        Non-Goals:
        {(item.NonGoals.Count == 0 ? "(none)" : string.Join("\n", item.NonGoals.Select((n, i) => $"{i + 1}. {n}")))}
        
        ---
        
        Generate the complete QA plan.
        IMPORTANT: NO PLACEHOLDER CONTENT. Every section must have specific test cases and details.
        """;

        var provider = PersonaLlmProviderFactory.Create(PersonaId.QA, modelConfig);
        
        var lmRequest = new LanguageModelRequest(
            provider.PersonaId,
            personaPrompt,
            flowPrompt,
            context);

        var response = provider.GenerateAsync(lmRequest, CancellationToken.None).GetAwaiter().GetResult();
        
        var qaPlan = CleanMarkdownOutput(response.OutputText);
        
        if (!Directory.Exists(runDir))
            Directory.CreateDirectory(runDir);
        
        File.WriteAllText(artifactPath, qaPlan);
        
        return qaPlan;
    }

    private string GenerateTechnicalTasks(
        RefineTechRequest request,
        BacklogItem item,
        EstimateModel estimate,
        PersonaModelConfig modelConfig,
        string runDir)
    {
        var artifactPath = Path.Combine(runDir, "technical-tasks.yaml");
        
        if (File.Exists(artifactPath))
        {
            return File.ReadAllText(artifactPath);
        }

        var flowPrompt = LoadFlowPrompt(request.Workdir, "refine-tech.md");
        var personaPrompt = LoadPersonaPrompt(request.Workdir, PersonaId.SAD, "sad-technical-tasks.md");
        
        var context = $"""
        BACKLOG ITEM:
        - ID: {item.Id}
        - Title: {item.Title}
        - Story: {item.Story}
        
        Acceptance Criteria:
        {(item.AcceptanceCriteria.Count == 0 ? "(none)" : string.Join("\n", item.AcceptanceCriteria.Select((c, i) => $"{i + 1}. {c}")))}
        
        Total Story Points: {estimate.StoryPoints}
        
        Dependencies:
        {(item.Dependencies.Count == 0 ? "(none)" : string.Join("\n", item.Dependencies.Select((d, i) => $"{i + 1}. {d}")))}
        
        ---
        
        Generate the technical tasks in YAML format.
        IMPORTANT: NO PLACEHOLDER TASKS. Each task must be specific and actionable.
        Total tasks should align with story points: 1 point = 1 day, 3 points = 3-5 days, 5 points = 1 week.
        """;

        var provider = PersonaLlmProviderFactory.Create(PersonaId.SAD, modelConfig);
        
        var lmRequest = new LanguageModelRequest(
            provider.PersonaId,
            personaPrompt,
            flowPrompt,
            context);

        var response = provider.GenerateAsync(lmRequest, CancellationToken.None).GetAwaiter().GetResult();
        
        var tasks = CleanYamlOutput(response.OutputText);
        
        if (!Directory.Exists(runDir))
            Directory.CreateDirectory(runDir);
        
        File.WriteAllText(artifactPath, tasks);
        
        return tasks;
    }

    private string CleanMarkdownOutput(string output)
    {
        var cleaned = output.Trim();
        
        if (cleaned.StartsWith("```markdown"))
            cleaned = cleaned.Substring("```markdown".Length);
        if (cleaned.StartsWith("```"))
            cleaned = cleaned.Substring("```".Length);
        
        if (cleaned.EndsWith("```"))
            cleaned = cleaned.Substring(0, cleaned.Length - 3);
        
        return cleaned.Trim();
    }

    private string CleanYamlOutput(string output)
    {
        var cleaned = output.Trim();
        
        if (cleaned.StartsWith("```yaml"))
            cleaned = cleaned.Substring("```yaml".Length);
        if (cleaned.StartsWith("```"))
            cleaned = cleaned.Substring("```".Length);
        
        if (cleaned.EndsWith("```"))
            cleaned = cleaned.Substring(0, cleaned.Length - 3);
        
        return cleaned.Trim();
    }

    private const int MaxEstimationIterations = 3;

    private EstimateModel RunEstimationConsensus(
        RefineTechRequest request,
        BacklogItem item,
        string runDir)
    {
        var flowPrompt = LoadFlowPrompt(request.Workdir, "refine-tech.md");
        var modelConfig = request.GetModelConfig();
        
        var iterations = new List<EstimationRoundResult>();
        EstimationRoundResult? previousRound = null;
        
        for (int round = 1; round <= MaxEstimationIterations; round++)
        {
            var roundResult = RunEstimationRound(
                request, item, runDir, flowPrompt, modelConfig, round, previousRound);
            
            iterations.Add(roundResult);
            
            // Check convergence: all estimates within 1 point
            if (round > 1 && IsConverged(roundResult.Estimates))
            {
                break;
            }
            
            previousRound = roundResult;
        }
        
        var finalStoryPoints = ComputeFinalEstimate(iterations);
        
        // Build the final estimate model
        // Use SAD's architectural recommendation (first in EstimationTeam)
        var lastRound = iterations.Last();
        var sadEstimate = lastRound.Estimates.ContainsKey(PersonaId.SAD) 
            ? lastRound.Estimates[PersonaId.SAD] 
            : lastRound.Estimates.Values.First();
        
        var finalEstimate = new EstimateModel
        {
            StoryPoints = finalStoryPoints,
            Scale = "fibonacci",
            Confidence = sadEstimate.Confidence,
            RiskLevel = "medium",
            ComplexityDrivers = sadEstimate.Rationale != "" 
                ? new List<string> { sadEstimate.Rationale } 
                : new List<string>(),
            Assumptions = new List<string>(),
            Dependencies = new List<string>(),
            NonGoals = new List<string>(),
            Notes = $"Consensus from {iterations.Count} iteration(s). Converged: {IsConverged(lastRound.Estimates)}",
            Rationale = $"Final estimate after {iterations.Count} rounds. Architecture from SAD recommendation.",
            CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            CreatedFromRunId = request.RunId,
            PersonaId = "consensus",
            
            // Use SAD's architectural recommendation
            AppType = sadEstimate.AppType,
            Language = sadEstimate.Language,
            Runtime = sadEstimate.Runtime,
            Framework = sadEstimate.Framework,
            Projects = sadEstimate.Projects
        };
        
        // Write iteration history for audit
        var votingRecord = new EstimationVotingRecord
        {
            Iterations = iterations.Select((r, i) => new IterationRecord
            {
                RoundNumber = i + 1,
                Estimates = r.Estimates.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => new VotingRecordEntry
                    {
                        Persona = kvp.Value.PersonaId,
                        StoryPoints = kvp.Value.StoryPoints,
                        Confidence = kvp.Value.Confidence,
                        Rationale = kvp.Value.Rationale,
                        ModelUsed = kvp.Value.ModelUsed,
                        RawResponse = kvp.Value.RawResponse
                    })
            }).ToList(),
            FinalEstimate = finalEstimate.StoryPoints,
            Converged = IsConverged(iterations.Last().Estimates),
            ComputedAtUtc = DateTimeOffset.UtcNow.ToString("O")
        };
        
        _runArtifactStore.WriteJson(runDir, "estimation.voting.json", votingRecord);
        
        finalEstimate.EstimateId = $"EST-{request.RunId}-{request.ItemId}-CONSENSUS";
        
        return finalEstimate;
    }

    private EstimationRoundResult RunEstimationRound(
        RefineTechRequest request,
        BacklogItem item,
        string runDir,
        string flowPrompt,
        PersonaModelConfig modelConfig,
        int roundNumber,
        EstimationRoundResult? previousRound)
    {
        var context = BuildEstimationContext(item, previousRound);
        
        var roundEstimates = new Dictionary<PersonaId, PersonaEstimateResult>();
        
        foreach (var persona in EstimationTeam)
        {
            var provider = PersonaLlmProviderFactory.Create(persona, modelConfig);
            var personaPrompt = LoadPersonaPrompt(request.Workdir, persona);

            var contractInstruction = GetEstimationContractInstruction(persona, roundNumber > 1);
            
            var lmRequest = new LanguageModelRequest(
                provider.PersonaId,
                personaPrompt,
                flowPrompt + "\n\n" + contractInstruction,
                context);

            var response = provider.GenerateAsync(lmRequest, CancellationToken.None).GetAwaiter().GetResult();

            var parsed = ParseEstimationResponse(response.OutputText, request.RunId, request.ItemId);
            parsed.ModelUsed = provider.ModelUsed;
            
            var personaResult = new PersonaEstimateResult
            {
                PersonaId = parsed.PersonaId,
                StoryPoints = parsed.StoryPoints,
                Confidence = parsed.Confidence,
                Rationale = parsed.Rationale,
                ModelUsed = parsed.ModelUsed,
                RawResponse = response.OutputText,
                AppType = parsed.AppType,
                Language = parsed.Language,
                Runtime = parsed.Runtime,
                Framework = parsed.Framework,
                Projects = parsed.Projects
            };
            
            roundEstimates[persona] = personaResult;
        }
        
        return new EstimationRoundResult
        {
            RoundNumber = roundNumber,
            Estimates = roundEstimates
        };
    }

    private string BuildEstimationContext(BacklogItem item, EstimationRoundResult? previousRound)
    {
        var context = $"""
        Backlog Item:
        - Id: {item.Id}
        - Title: {item.Title}
        - Status: {item.Status}
        - Priority: {item.Priority}
        - Size: {item.Size}
        - Owner: {item.Owner}

        Story:
        {item.Story}

        Acceptance Criteria:
        {(item.AcceptanceCriteria.Count == 0 ? "(none)" : string.Join("; ", item.AcceptanceCriteria))}

        Non-goals:
        {(item.NonGoals.Count == 0 ? "(none)" : string.Join("; ", item.NonGoals))}

        Dependencies:
        {(item.Dependencies.Count == 0 ? "(none)" : string.Join("; ", item.Dependencies))}

        Risks:
        {(item.Risks.Count == 0 ? "(none)" : string.Join("; ", item.Risks))}
        """;
        
        if (previousRound != null)
        {
            context += "\n\n---\n\nOTHER TEAM MEMBERS' ESTIMATES:\n";
            foreach (var (persona, estimate) in previousRound.Estimates)
            {
                context += $"\n{persona}: {estimate.StoryPoints} points (Confidence: {estimate.Confidence})\n";
                context += $"Rationale: {estimate.Rationale}\n";
            }
            context += "\nConsider these estimates when providing your own. If you agree with another estimate, you may keep it. If you disagree, provide your reasoning.";
        }
        
        return context;
    }

    private bool IsConverged(Dictionary<PersonaId, PersonaEstimateResult> estimates)
    {
        var points = estimates.Values.Select(e => e.StoryPoints).OrderBy(x => x).ToList();
        return points.Max() - points.Min() <= 1;
    }

    private int ComputeFinalEstimate(List<EstimationRoundResult> iterations)
    {
        var lastRound = iterations.Last();
        var estimates = lastRound.Estimates.Values.Select(e => e.StoryPoints).ToList();
        
        // Majority wins, tie = largest
        var grouped = estimates
            .GroupBy(e => e)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key);
        
        return grouped.First().Key;
    }

    private string GetEstimationContractInstruction(PersonaId persona, bool includePreviousRound = false)
    {
        var instruction = """
        OUTPUT RULES (NON-NEGOTIABLE):
        - Output MUST be a SINGLE JSON object.
        - Do NOT wrap in markdown.
        - Do NOT include any prose before or after the JSON.
        - storyPoints MUST be a Fibonacci number: 1, 3, or 5
        - If the item is too large, return 5 and explain in notes what needs to be broken down
        
        Required keys:
        - storyPoints: integer (1, 3, or 5)
        - confidence: string ("low", "medium", "high")
        - complexityDrivers: array of strings describing what makes this item complex
        - assumptions: array of strings describing what you're assuming to be true
        - dependencies: array of strings describing external dependencies
        - rationale: string explaining your estimate (THIS IS IMPORTANT FOR TEAM DISCUSSION)
        """;
        
        if (includePreviousRound)
        {
            instruction += "\n- IMPORTANT: You have seen other team members' estimates. If you're adjusting your estimate based on theirs, explain why in the rationale field.";
        }
        
        return instruction;
    }

    private string BuildEstimationContext(BacklogItem item)
    {
        return $"""
        Backlog Item:
        - Id: {item.Id}
        - Title: {item.Title}
        - Status: {item.Status}
        - Priority: {item.Priority}
        - Size: {item.Size}
        - Owner: {item.Owner}

        Story:
        {item.Story}

        Acceptance Criteria:
        {(item.AcceptanceCriteria.Count == 0 ? "(none)" : string.Join("; ", item.AcceptanceCriteria))}

        Non-goals:
        {(item.NonGoals.Count == 0 ? "(none)" : string.Join("; ", item.NonGoals))}

        Dependencies:
        {(item.Dependencies.Count == 0 ? "(none)" : string.Join("; ", item.Dependencies))}

        Risks:
        {(item.Risks.Count == 0 ? "(none)" : string.Join("; ", item.Risks))}

        ---
        
        IMPORTANT: You are part of the technical estimation team. Provide your Fibonacci estimate (1, 3, or 5) based on the above information.
        """;
    }

    private string GetEstimationContractInstruction(PersonaId persona)
    {
        return """
        OUTPUT RULES (NON-NEGOTIABLE):
        - Output MUST be a SINGLE JSON object.
        - Do NOT wrap in markdown.
        - Do NOT include any prose before or after the JSON.
        - storyPoints MUST be a Fibonacci number: 1, 3, or 5
        - If the item is too large, return 5 and explain in notes what needs to be broken down
        
        Required keys:
        - storyPoints: integer (1, 3, or 5)
        - confidence: string ("low", "medium", "high")
        - complexityDrivers: array of strings describing what makes this item complex
        - assumptions: array of strings describing what you're assuming to be true
        - dependencies: array of strings describing external dependencies
        - notes: string with any additional context
        """;
    }

    private EstimateModel ParseEstimationResponse(string responseText, string runId, int itemId)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            return new EstimateModel
            {
                EstimateId = $"EST-{runId}-{itemId}",
                StoryPoints = root.TryGetProperty("storyPoints", out var sp) ? sp.GetInt32() : 3,
                Scale = "fibonacci",
                Confidence = root.TryGetProperty("confidence", out var conf) ? conf.GetString() ?? "medium" : "medium",
                RiskLevel = "medium",
                ComplexityDrivers = ParseStringArray(root, "complexityDrivers"),
                Assumptions = ParseStringArray(root, "assumptions"),
                Dependencies = ParseStringArray(root, "dependencies"),
                NonGoals = new List<string>(),
                Notes = root.TryGetProperty("notes", out var notes) ? notes.GetString() ?? "" : "",
                Rationale = root.TryGetProperty("rationale", out var rationale) ? rationale.GetString() ?? "" : "",
                CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                CreatedFromRunId = runId,
                PersonaId = "unknown",
                
                // Architecture decisions from LLM
                AppType = root.TryGetProperty("appType", out var at) ? at.GetString() ?? "" : "",
                Language = root.TryGetProperty("language", out var lang) ? lang.GetString() ?? "csharp" : "csharp",
                Runtime = root.TryGetProperty("runtime", out var rt) ? rt.GetString() ?? "net8.0" : "net8.0",
                Framework = root.TryGetProperty("framework", out var fw) ? fw.GetString() ?? "" : "",
                Projects = ParseProjects(root)
            };
        }
        catch
        {
            return new EstimateModel
            {
                EstimateId = $"EST-{runId}-{itemId}-FALLBACK",
                StoryPoints = 3,
                Scale = "fibonacci",
                Confidence = "medium",
                RiskLevel = "medium",
                ComplexityDrivers = new List<string> { "Parse error - using fallback" },
                Assumptions = new List<string> { "Parse error - using fallback" },
                Dependencies = new List<string>(),
                NonGoals = new List<string>(),
                Notes = $"Failed to parse LLM response. Response: {responseText}",
                Rationale = "Parse error - using fallback estimate",
                CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                CreatedFromRunId = runId,
                PersonaId = "parse-error"
            };
        }
    }

    private List<ProjectSpec> ParseProjects(JsonElement root)
    {
        var projects = new List<ProjectSpec>();
        
        if (!root.TryGetProperty("projects", out var projectsProp))
            return projects;
            
        if (projectsProp.ValueKind != JsonValueKind.Array)
            return projects;
            
        foreach (var proj in projectsProp.EnumerateArray())
        {
            if (proj.ValueKind != JsonValueKind.Object)
                continue;
                
            var spec = new ProjectSpec
            {
                Name = proj.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Type = proj.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
                Path = proj.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "",
                Dependencies = new List<string>()
            };
            
            if (proj.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
            {
                foreach (var dep in deps.EnumerateArray())
                {
                    if (dep.ValueKind == JsonValueKind.String)
                        spec.Dependencies.Add(dep.GetString() ?? "");
                }
            }
            
            projects.Add(spec);
        }
        
        return projects;
    }

    private List<string> ParseStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var prop))
            return new List<string>();

        if (prop.ValueKind != JsonValueKind.Array)
            return new List<string>();

        return prop.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString() ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private int ComputeConsensus(List<int> estimates)
    {
        var grouped = estimates
            .GroupBy(e => e)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key)
            .ToList();

        return grouped[0].Key;
    }

    private string LoadFlowPrompt(string workdir, string promptFileName)
    {
        var path = Path.Combine(workdir, "prompts", "flows", promptFileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Flow prompt not found: {path}");
        return File.ReadAllText(path);
    }

    private string LoadPersonaPrompt(string workdir, PersonaId personaId)
    {
        var promptFileName = personaId switch
        {
            PersonaId.SAD => "senior-architect-dev.md",
            PersonaId.SASD => "senior-audio-dev.md",
            PersonaId.QA => "qa-engineer.md",
            _ => throw new ArgumentException($"No prompt file for persona: {personaId}")
        };

        var path = Path.Combine(workdir, "prompts", "personas", promptFileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Persona prompt not found: {path}");
        return File.ReadAllText(path);
    }

    private string LoadPersonaPrompt(string workdir, PersonaId personaId, string customPromptFile)
    {
        var path = Path.Combine(workdir, "prompts", "personas", customPromptFile);
        if (!File.Exists(path))
        {
            return LoadPersonaPrompt(workdir, personaId);
        }
        return File.ReadAllText(path);
    }
}

// Local DTO for estimate (internal only)
public sealed class EstimateModel
{
    public string EstimateId { get; set; } = "";
    public int StoryPoints { get; set; }
    public string Scale { get; set; } = "";
    public string Confidence { get; set; } = "";
    public string RiskLevel { get; set; } = "";
    public List<string> ComplexityDrivers { get; set; } = new();
    public List<string> Assumptions { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public List<string> NonGoals { get; set; } = new();
    public string Notes { get; set; } = "";
    public string Rationale { get; set; } = "";
    public string ModelUsed { get; set; } = "";
    public string CreatedAtUtc { get; set; } = "";
    public string CreatedFromRunId { get; set; } = "";
    public string PersonaId { get; set; } = "";
    
    // Architecture decisions from LLM
    public string AppType { get; set; } = "";
    public string Language { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string Framework { get; set; } = "";
    public List<ProjectSpec> Projects { get; set; } = new();
}

public class ProjectSpec
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> Dependencies { get; set; } = new();
}

public sealed class EstimationVotingRecord
{
    public List<IterationRecord> Iterations { get; set; } = new();
    public int FinalEstimate { get; set; }
    public bool Converged { get; set; }
    public string ComputedAtUtc { get; set; } = "";
}

public sealed class IterationRecord
{
    public int RoundNumber { get; set; }
    public Dictionary<string, VotingRecordEntry> Estimates { get; set; } = new();
}

public sealed class VotingRecordEntry
{
    public string Persona { get; set; } = "";
    public int StoryPoints { get; set; }
    public string Confidence { get; set; } = "";
    public string Rationale { get; set; } = "";
    public string ModelUsed { get; set; } = "";
    public string RawResponse { get; set; } = "";
}

public class EstimationRoundResult
{
    public int RoundNumber { get; set; }
    public Dictionary<PersonaId, PersonaEstimateResult> Estimates { get; set; } = new();
}

public class PersonaEstimateResult
{
    public string PersonaId { get; set; } = "";
    public int StoryPoints { get; set; }
    public string Confidence { get; set; } = "";
    public string Rationale { get; set; } = "";
    public string ModelUsed { get; set; } = "";
    public string RawResponse { get; set; } = "";
    
    // Architecture decisions from LLM
    public string AppType { get; set; } = "";
    public string Language { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string Framework { get; set; } = "";
    public List<ProjectSpec> Projects { get; set; } = new();
}
