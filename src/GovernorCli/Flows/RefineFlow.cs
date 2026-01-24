using GovernorCli.LanguageModel;
using GovernorCli.Personas;
using GovernorCli.Prompts;
using GovernorCli.Runs;
using GovernorCli.State;
using GovernorCli.Validation;

namespace GovernorCli.Flows;

public static class RefineFlow
{
    // Exit codes:
    // 0 success
    // 2 invalid repo layout
    // 3 item not found
    // 4 backlog parse error
    // 5 DoR gate failed
    // 6 prompt load error (new)
    public static int Run(string workdir, int itemId, bool verbose)
    {
        // Validate layout first
        var problems = RepoChecks.ValidateLayout(workdir);
        if (problems.Count > 0)
            return 2;

        // Load backlog
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

        // DoR gate
        var dorErrors = DefinitionOfReady.Validate(item);
        if (dorErrors.Count > 0)
        {
            WriteDorFailRun(workdir, itemId, item, dorErrors);
            return 5;
        }

        // Run id: safe for Windows paths (no ':' characters)
        var utc = DateTimeOffset.UtcNow;
        var runId = $"{utc:yyyyMMdd_HHmmss}_refine_item-{itemId}";

        var runsRoot = Path.Combine(workdir, "state", "runs");
        var runDir = RunWriter.CreateRunFolder(runsRoot, runId);

        // Run record
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

        // Base refine artifact for humans
        RunWriter.WriteText(runDir, "refine.md", BuildRefineMarkdown(runId, record, item));

        // ---- NEW: persona turns (stub provider) ----
        string flowPrompt;
        try
        {
            flowPrompt = PromptLoader.LoadFlowPrompt(workdir, "refine.md");
        }
        catch
        {
            // If prompts missing, we want this to be a deterministic failure.
            // Still keep the run folder for traceability.
            RunWriter.WriteText(runDir, "summary.md", "# Refine Summary\n\nFAIL: Could not load prompts/flows/refine.md");
            return 6;
        }

        // Minimal input context for now (we tighten later)
        var inputContext = BuildInputContext(item);

        ILanguageModelProvider provider = new StubLanguageModelProvider();
        var turnsDir = TurnWriter.EnsureTurnsDir(runDir);

        var turnOutputs = new List<(string PersonaId, string OutputText)>();
        var ct = CancellationToken.None;

        var turnIndex = 1;
        foreach (var persona in PersonaCatalog.RefinementOrder)
        {
            string personaPrompt;
            try
            {
                personaPrompt = PromptLoader.LoadPersonaPrompt(workdir, persona.PromptFileName);
            }
            catch
            {
                // Log the failure as a turn artifact (still auditable)
                var failPayload = new
                {
                    turn = turnIndex,
                    persona = persona.Id.ToString(),
                    personaDisplayName = persona.DisplayName,
                    provider = provider.Name,
                    createdAtUtc = utc.ToString("O"),
                    error = $"Missing prompt file: prompts/personas/{persona.PromptFileName}"
                };

                TurnWriter.WriteTurn(turnsDir, turnIndex, persona.Id.ToString(), failPayload);
                RunWriter.WriteText(runDir, "summary.md",
                    $"# Refine Summary\n\nFAIL: Missing persona prompt file prompts/personas/{persona.PromptFileName}");
                return 6;
            }

            var request = new LanguageModelRequest(
                PersonaId: persona.Id.ToString(),
                PersonaPrompt: personaPrompt,
                FlowPrompt: flowPrompt,
                InputContext: inputContext
            );

            var response = provider.GenerateAsync(request, ct).GetAwaiter().GetResult();

            var payload = new
            {
                turn = turnIndex,
                persona = persona.Id.ToString(),
                personaDisplayName = persona.DisplayName,
                provider = provider.Name,
                createdAtUtc = utc.ToString("O"),
                request = new
                {
                    flowPromptFile = "prompts/flows/refine.md",
                    personaPromptFile = $"prompts/personas/{persona.PromptFileName}"
                },
                response = new
                {
                    text = response.OutputText,
                    metadata = response.Metadata
                }
            };

            TurnWriter.WriteTurn(turnsDir, turnIndex, persona.Id.ToString(), payload);
            turnOutputs.Add((persona.Id.ToString(), response.OutputText));

            turnIndex++;
        }

        // Summary for humans (stub outputs)
        var summary = BuildSummaryMarkdown(runId, provider.Name, turnOutputs);
        RunWriter.WriteText(runDir, "summary.md", summary);

        // Mark run record status as completed (optional update)
        record.Status = "completed";
        RunWriter.WriteJson(runDir, "run.json", record);

        return 0;
    }

    private static void WriteDorFailRun(string workdir, int itemId, BacklogItem item, List<string> dorErrors)
    {
        var utc = DateTimeOffset.UtcNow;
        var runId = $"{utc:yyyyMMdd_HHmmss}_refine_item-{itemId}_DOR_FAIL";
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
            Status = "dor_failed"
        };

        RunWriter.WriteJson(runDir, "run.json", record);

        var md = $"""
        # Refine Run (DoR Failed)

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

        ## DoR Errors
        {string.Join(Environment.NewLine, dorErrors.Select(e => $"- {e}"))}
        """;

        RunWriter.WriteText(runDir, "refine.md", md);
    }

    private static string BuildInputContext(BacklogItem item)
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
        {(item.Acceptance_Criteria.Count == 0 ? "(none)" : string.Join("; ", item.Acceptance_Criteria))}

        Non-goals:
        {(item.Non_Goals.Count == 0 ? "(none)" : string.Join("; ", item.Non_Goals))}
        """;
    }

    private static string BuildRefineMarkdown(string runId, RunRecord record, BacklogItem item)
    {
        return $"""
        # Refine Run

        - RunId: {runId}
        - CreatedAtUtc: {record.CreatedAtUtc}
        - Flow: refine
        - Workdir: {record.Workdir}

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
    }

    private static string BuildSummaryMarkdown(string runId, string providerName, List<(string PersonaId, string OutputText)> turnOutputs)
    {
        return $"""
        # Refine Summary

        RunId: {runId}
        Provider: {providerName}

        {string.Join(Environment.NewLine + Environment.NewLine, turnOutputs.Select(t => $"## {t.PersonaId}\n{t.OutputText}"))}
        """;
    }
}
