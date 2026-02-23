using GovernorCli.Application.Models;
using GovernorCli.Application.Stores;
using GovernorCli.Domain.Exceptions;
using GovernorCli.State;

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
}

public class RefineTechResult
{
    public bool Success { get; set; }
    public string RunId { get; set; } = "";
    public PatchPreview? Patch { get; set; }
}

/// <summary>
/// Business logic for technical readiness review (TRR).
/// Responsibility: Compute estimates, generate artifacts, optionally persist.
/// Zero responsibility: File I/O (via stores), orchestration (via Flow).
/// </summary>
public class RefineTechUseCase
{
    private readonly IBacklogStore _backlogStore;
    private readonly IRunArtifactStore _runArtifactStore;

    public RefineTechUseCase(
        IBacklogStore backlogStore,
        IRunArtifactStore runArtifactStore)
    {
        _backlogStore = backlogStore;
        _runArtifactStore = runArtifactStore;
    }

    /// <summary>
    /// Process technical readiness review.
    /// Returns typed PatchPreview for all cases (approved or not).
    /// </summary>
    public RefineTechResult Process(RefineTechRequest request)
    {
        var utc = DateTimeOffset.UtcNow;

        // Load backlog (via store, abstract)
        var backlog = _backlogStore.Load(request.BacklogPath);

        // Validate item exists
        var item = backlog.Backlog.FirstOrDefault(x => x.Id == request.ItemId)
            ?? throw new ItemNotFoundException(request.ItemId);

        // Create run directory
        var runDir = _runArtifactStore.CreateRunFolder(request.RunsDir, request.RunId);

        // 1) Generate placeholder artifacts
        var estimate = BuildPlaceholderEstimate(request.RunId, request.ItemId, utc);
        _runArtifactStore.WriteJson(runDir, "estimation.json", estimate);
        _runArtifactStore.WriteText(runDir, "architecture.md", BuildArchitectureTemplate(item));
        _runArtifactStore.WriteText(runDir, "qa-plan.md", BuildQaPlanTemplate(item));
        _runArtifactStore.WriteText(runDir, "technical-tasks.yaml", 
            BuildTasksTemplate(request.RunId, request.ItemId, estimate.EstimateId));

        // 2) Compute patch preview (read-only)
        var patch = ComputePatchPreview(item, estimate, request.RunId, utc);
        _runArtifactStore.WriteJson(runDir, "patch.preview.json", patch);

        // 3) If not approved, done
        if (!request.Approve)
        {
            _runArtifactStore.WriteText(runDir, "summary.md",
                $"# Refine-Tech Summary\n\n" +
                $"✓ Preview generated for item {request.ItemId}.\n\n" +
                $"Next: Approve changes with:\n" +
                $"  governor refine-tech --item {request.ItemId} --approve\n");

            return new RefineTechResult
            {
                Success = true,
                RunId = request.RunId,
                Patch = patch
            };
        }

        // 4) Apply patch to backlog
        ApplyPatchAndPersist(request.BacklogPath, backlog, request.ItemId, estimate, request.RunId);

        // 5) Write applied patch (typed, authoritative record)
        var appliedPatch = new AppliedPatch
        {
            AppliedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            ItemId = request.ItemId,
            RunId = request.RunId,
            Changes = patch.Changes
        };

        _runArtifactStore.WriteJson(runDir, "patch.json", appliedPatch);

        // 6) Write success summary
        _runArtifactStore.WriteText(runDir, "summary.md",
            $"# Refine-Tech Summary\n\n" +
            $"✓ Approved and applied for item {request.ItemId}.\n\n" +
            $"Changes:\n" +
            $"- Estimate embedded\n" +
            $"- Status → ready_for_dev\n" +
            $"- Technical notes ref → runs/{request.RunId}/\n");

        return new RefineTechResult
        {
            Success = true,
            RunId = request.RunId,
            Patch = patch
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

    private void ApplyPatchAndPersist(
        string backlogPath,
        BacklogFile backlog,
        int itemId,
        EstimateModel estimate,
        string runId)
    {
        var item = backlog.Backlog.First(x => x.Id == itemId);

        item.Status = "ready_for_dev";
        item.TechnicalNotesRef = $"runs/{runId}/";
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
    public string CreatedAtUtc { get; set; } = "";
    public string CreatedFromRunId { get; set; } = "";
}
