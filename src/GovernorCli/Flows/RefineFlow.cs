using GovernorCli.Runs;
using GovernorCli.State;

namespace GovernorCli.Flows;

public static class RefineFlow
{
    // Exit codes:
    // 0 success
    // 2 invalid repo layout
    // 3 item not found
    // 4 backlog parse error
    public static int Run(string workdir, int itemId, bool verbose)
    {
        // Validate layout first
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

        // Run id: safe for Windows paths (no ':' characters)
        var utc = DateTimeOffset.UtcNow;
        var runId = $"{utc:yyyyMMdd_HHmmss}_refine_item-{itemId}";

        var runsRoot = Path.Combine(workdir, "state", "runs");
        var runDir = RunWriter.CreateRunFolder(runsRoot, runId);

        var record = new RunRecord
        {
            RunId = runId,
            Flow = "refine",
            CreatedAtUtc = utc.ToString("O"),
            Workdir = workdir,
            ItemId = item.Id,
            ItemTitle = item.Title,
            Status = "created"
        };

        RunWriter.WriteJson(runDir, "run.json", record);

        var md = $"""
        # Refine Run

        - RunId: {runId}
        - CreatedAtUtc: {record.CreatedAtUtc}
        - Flow: refine
        - Workdir: {workdir}

        ## Backlog Item
        - Id: {item.Id}
        - Title: {item.Title}
        - Status: {item.Status}
        - Priority: {item.Priority}
        - Size: {item.Size}
        - Owner: {item.Owner}

        ## Story
        {item.Story}

        ## Acceptance Criteria
        {(item.Acceptance_Criteria.Count == 0 ? "- (none)" : string.Join(Environment.NewLine, item.Acceptance_Criteria.Select(x => $"- {x}")))}

        ## Non-goals
        {(item.Non_Goals.Count == 0 ? "- (none)" : string.Join(Environment.NewLine, item.Non_Goals.Select(x => $"- {x}")))}

        ## Dependencies
        {(item.Dependencies.Count == 0 ? "- (none)" : string.Join(Environment.NewLine, item.Dependencies.Select(x => $"- {x}")))}

        ## Risks
        {(item.Risks.Count == 0 ? "- (none)" : string.Join(Environment.NewLine, item.Risks.Select(x => $"- {x}")))}

        """;

        RunWriter.WriteText(runDir, "refine.md", md);

        // Also write a small pointer file for humans
        RunWriter.WriteText(runDir, "README.txt", "This folder contains the artifacts for a single Governor run.");

        return 0;
    }
}
