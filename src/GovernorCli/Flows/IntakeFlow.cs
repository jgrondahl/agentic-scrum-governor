using GovernorCli.Runs;
using GovernorCli.State;

namespace GovernorCli.Flows;

public static class IntakeFlow
{
    // Exit codes:
    // 0 success
    // 2 invalid repo layout
    // 4 backlog parse error
    // 8 apply/update backlog failed
    public static int Run(string workdir, string title, string story, bool verbose)
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

        // Compute next id
        var nextId = backlog.Backlog.Count == 0 ? 1 : backlog.Backlog.Max(x => x.Id) + 1;

        // Create item (minimal defaults)
        var item = new BacklogItem
        {
            Id = nextId,
            Title = title.Trim(),
            Status = "candidate",
            Priority = 1,
            Size = "S",
            Owner = "PO",
            Story = story.Trim(),
            AcceptanceCriteria = new List<string>(),
            NonGoals = new List<string>(),
            Dependencies = new List<string>(),
            Risks = new List<string>()
        };

        // Create run folder
        var utc = DateTimeOffset.UtcNow;
        var runId = $"{utc:yyyyMMdd_HHmmss}_intake_item-{nextId}";
        var runDir = RunWriter.CreateRunFolder(Path.Combine(workdir, "state", "runs"), runId);

        var record = new RunRecord
        {
            RunId = runId,
            Flow = "intake",
            CreatedAtUtc = utc.ToString("O"),
            Workdir = workdir,
            ItemId = item.Id,
            ItemTitle = item.Title,
            Status = "created"
        };

        RunWriter.WriteJson(runDir, "run.json", record);

        var md = $"""
        # Intake Run

        - RunId: {runId}
        - CreatedAtUtc: {record.CreatedAtUtc}
        - Flow: intake
        - Workdir: {workdir}

        ## New Backlog Item
        - Id: {item.Id}
        - Title: {item.Title}
        - Status: {item.Status}
        - Priority: {item.Priority}
        - Size: {item.Size}
        - Owner: {item.Owner}

        ## Story
        {item.Story}
        """;

        RunWriter.WriteText(runDir, "intake.md", md);

        // Persist backlog update
        try
        {
            backlog.Backlog.Add(item);
            BacklogSaver.Save(backlogPath, backlog);
        }
        catch (Exception ex)
        {
            RunWriter.WriteText(runDir, "summary.md", $"FAIL: Could not write backlog.yaml\n\n{ex.Message}");
            record.Status = "apply_failed";
            RunWriter.WriteJson(runDir, "run.json", record);
            return 8;
        }

        record.Status = "completed";
        RunWriter.WriteJson(runDir, "run.json", record);

        // Write a simple pointer
        RunWriter.WriteText(runDir, "summary.md",
            $"OK: Created backlog item {item.Id}. Next: run `refine --item {item.Id}`.");

        return 0;
    }
}
