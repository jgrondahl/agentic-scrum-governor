using GovernorCli.Runs;
using GovernorCli.State;

namespace GovernorCli.Flows;

public static class RefineTechFlow
{
    // Exit codes:
    // 0 success
    // 2 repo layout invalid
    // 3 item not found
    // 4 backlog parse error
    // 8 apply failed
    public static int Run(string workdir, int itemId, bool verbose, bool approve)
    {
        var problems = RepoChecks.ValidateLayout(workdir);
        if (problems.Count > 0)
            return 2;

        var backlogPath = Path.Combine(workdir, "state", "backlog.yaml");

        BacklogFile backlog;
        try
        {
            backlog = BacklogLoader.Load(backlogPath);
        }
        catch
        {
            return 4;
        }

        var item = backlog.Backlog.FirstOrDefault(x => x.Id == itemId);
        if (item is null)
            return 3;

        var utc = DateTimeOffset.UtcNow;
        var runId = $"{utc:yyyyMMdd_HHmmss}_refine-tech_item-{itemId}";
        var runDir = RunWriter.CreateRunFolder(Path.Combine(workdir, "state", "runs"), runId);

        // 1) Write placeholder artifacts (valid + minimal)
        var estimate = BuildPlaceholderEstimate(runId, itemId, utc);

        RunWriter.WriteJson(runDir, "estimation.json", estimate);
        RunWriter.WriteText(runDir, "architecture.md", BuildArchitectureTemplate(item));
        RunWriter.WriteText(runDir, "qa-plan.md", BuildQaPlanTemplate(item));
        RunWriter.WriteText(runDir, "technical-tasks.yaml", BuildTasksTemplate(runId, itemId, estimate.estimateId));

        // 2) Compute patch preview (no mutation)
        var preview = ComputePatchPreview(item, estimate, runId, utc);
        RunWriter.WriteJson(runDir, "patch.preview.json", preview);

        // 3) Apply only if approved
        if (!approve)
        {
            RunWriter.WriteText(runDir, "summary.md",
                $"# Refine-Tech Summary\n\nOK: Preview generated for item {itemId}.\n\nNo backlog changes applied (approval required).\n");
            return 0;
        }

        try
        {
            ApplyPatchAndPersist(workdir, backlogPath, backlog, itemId, estimate, runId);
        }
        catch (Exception ex)
        {
            RunWriter.WriteText(runDir, "summary.md",
                $"# Refine-Tech Summary\n\nFAIL: Could not apply patch.\n\n{ex.Message}");
            return 8;
        }

        // Write applied patch (authoritative record)
        RunWriter.WriteJson(runDir, "patch.json", new
        {
            appliedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            itemId,
            runId,
            applied = preview.Changes
        });

        // Decision log entry
        var approver = Environment.GetEnvironmentVariable("GOVERNOR_APPROVER") ?? "local";
        DecisionLog.Append(workdir,
            $"{DateTimeOffset.UtcNow:O} | refine-tech approved | item={itemId} | run={runId} | by={approver}");

        RunWriter.WriteText(runDir, "summary.md",
            $"# Refine-Tech Summary\n\nOK: Approved and applied for item {itemId}.\n\n- estimate embedded\n- status set to ready_for_dev\n- technical_notes_ref set\n");

        return 0;
    }

    // ---------- Patch logic ----------

    private static RefineTechPatch ComputePatchPreview(BacklogItem original, EstimateModel estimate, string runId, DateTimeOffset utc)
    {
        var beforeEstimate = original.Estimate;
        var afterEstimate = new BacklogEstimate
        {
            Id = estimate.estimateId,
            Story_Points = estimate.storyPoints,
            Scale = estimate.scale,
            Confidence = estimate.confidence,
            Risk_Level = estimate.riskLevel,
            Complexity_Drivers = estimate.complexityDrivers,
            Assumptions = estimate.assumptions,
            Dependencies = estimate.dependencies,
            Non_Goals = estimate.nonGoals,
            Notes = estimate.notes,
            Created_At_Utc = estimate.createdAtUtc,
            Created_From_Run_Id = estimate.createdFromRunId
        };

        var beforeStatus = original.Status ?? "candidate";
        var afterStatus = "ready_for_dev"; // explicit for tech readiness

        var beforeRef = original.Technical_Notes_Ref;
        var afterRef = $"runs/{runId}/";

        return new RefineTechPatch
        {
            ComputedAtUtc = utc.ToString("O"),
            ItemId = original.Id,
            Changes = new PatchChanges
            {
                Status = new StatusChange
                {
                    Before = beforeStatus,
                    After = afterStatus
                },
                Estimate = new EstimateChange
                {
                    Before = beforeEstimate,
                    After = afterEstimate
                },
                Technical_Notes_Ref = new RefChange
                {
                    Before = beforeRef,
                    After = afterRef
                }
            }
        };
    }

    private static void ApplyPatchAndPersist(
        string workdir,
        string backlogPath,
        BacklogFile backlog,
        int itemId,
        EstimateModel estimate,
        string runId)
    {
        var item = backlog.Backlog.First(x => x.Id == itemId);

        // status transition is part of tech readiness
        item.Status = "ready_for_dev";

        item.Technical_Notes_Ref = $"runs/{runId}/";

        item.Estimate = new BacklogEstimate
        {
            Id = estimate.estimateId,
            Story_Points = estimate.storyPoints,
            Scale = estimate.scale,
            Confidence = estimate.confidence,
            Risk_Level = estimate.riskLevel,
            Complexity_Drivers = estimate.complexityDrivers,
            Assumptions = estimate.assumptions,
            Dependencies = estimate.dependencies,
            Non_Goals = estimate.nonGoals,
            Notes = estimate.notes,
            Created_At_Utc = estimate.createdAtUtc,
            Created_From_Run_Id = estimate.createdFromRunId
        };

        BacklogSaver.Save(backlogPath, backlog);
    }

    // ---------- Placeholder artifact builders ----------

    private static EstimateModel BuildPlaceholderEstimate(string runId, int itemId, DateTimeOffset utc)
    {
        return new EstimateModel
        {
            estimateId = $"EST-{runId}-{itemId}",
            storyPoints = 5,
            scale = "fibonacci",
            confidence = "medium",
            riskLevel = "medium",
            complexityDrivers = new List<string> { "Placeholder: replace with real drivers during TRR." },
            assumptions = new List<string> { "Placeholder: list assumptions during TRR." },
            dependencies = new List<string>(),
            nonGoals = new List<string>(),
            notes = "Placeholder estimate produced by refine-tech skeleton.",
            createdAtUtc = utc.ToString("O"),
            createdFromRunId = runId
        };
    }

    private static string BuildArchitectureTemplate(BacklogItem item)
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

    private static string BuildQaPlanTemplate(BacklogItem item)
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

    private static string BuildTasksTemplate(string runId, int itemId, string estimateId)
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

    // ---------- Models for estimate.json ----------

    private sealed class EstimateModel
    {
        public string estimateId { get; set; } = "";
        public int storyPoints { get; set; }
        public string scale { get; set; } = "fibonacci";
        public string confidence { get; set; } = "medium";
        public string riskLevel { get; set; } = "medium";
        public List<string> complexityDrivers { get; set; } = new();
        public List<string> assumptions { get; set; } = new();
        public List<string> dependencies { get; set; } = new();
        public List<string> nonGoals { get; set; } = new();
        public string notes { get; set; } = "";
        public string createdAtUtc { get; set; } = "";
        public string createdFromRunId { get; set; } = "";
    }

    private sealed class RefineTechPatch
    {
        public string ComputedAtUtc { get; set; } = "";
        public int ItemId { get; set; }
        public PatchChanges Changes { get; set; } = new();
    }

    private sealed class PatchChanges
    {
        public StatusChange Status { get; set; } = new();
        public EstimateChange Estimate { get; set; } = new();
        public RefChange Technical_Notes_Ref { get; set; } = new();
    }

    private sealed class StatusChange
    {
        public string Before { get; set; } = "";
        public string After { get; set; } = "";
    }

    private sealed class EstimateChange
    {
        public BacklogEstimate? Before { get; set; }
        public BacklogEstimate? After { get; set; }
    }

    private sealed class RefChange
    {
        public string? Before { get; set; }
        public string After { get; set; } = "";
    }
}
