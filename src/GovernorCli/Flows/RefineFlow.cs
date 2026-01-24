using GovernorCli.LanguageModel;
using GovernorCli.Personas;
using GovernorCli.Prompts;
using GovernorCli.Runs;
using GovernorCli.State;
using GovernorCli.Validation;
using System.Text.Json;

namespace GovernorCli.Flows;

public static class RefineFlow
{
    // Exit codes:
    // 0 success
    // 2 invalid repo layout
    // 3 item not found
    // 4 backlog parse error
    // 5 DoR gate failed
    // 6 prompt load error
    // 7 contract validation failed (after retry)
    public static int Run(string workdir, int itemId, bool verbose)
    {
        // 1) Validate layout
        var problems = RepoChecks.ValidateLayout(workdir);
        if (problems.Count > 0)
            return 2;

        // 2) Load backlog
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

        // 3) Find item
        var item = backlog.Backlog.FirstOrDefault(x => x.Id == itemId);
        if (item is null)
            return 3;

        // 4) DoR gate
        var dorErrors = DefinitionOfReady.Validate(item);
        if (dorErrors.Count > 0)
        {
            WriteDorFailRun(workdir, itemId, item, dorErrors);
            return 5;
        }

        // 5) Create run folder + base artifacts
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
        RunWriter.WriteText(runDir, "refine.md", BuildRefineMarkdown(runId, record, item));

        // 6) Load prompts
        string flowPrompt;
        try
        {
            flowPrompt = PromptLoader.LoadFlowPrompt(workdir, "refine.md");
        }
        catch
        {
            RunWriter.WriteText(runDir, "summary.md", "# Refine Summary\n\nFAIL: Could not load prompts/flows/refine.md");
            record.Status = "prompt_load_failed";
            RunWriter.WriteJson(runDir, "run.json", record);
            return 6;
        }

        // 7) Prepare input context
        var inputContext = BuildInputContext(item);

        // 8) Provider (stub for now)
        ILanguageModelProvider provider = new StubLanguageModelProvider();
        var turnsDir = TurnWriter.EnsureTurnsDir(runDir);

        var turnOutputs = new List<(string PersonaId, string OutputText)>();
        var ct = CancellationToken.None;

        // 9) Execute turns with contract validation + one retry
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

                record.Status = "prompt_load_failed";
                RunWriter.WriteJson(runDir, "run.json", record);
                return 6;
            }

            // Two-attempt loop (attempt 1 + retry 1)
            var attempts = new List<object>();
            JsonElement? finalParsed = null;
            List<string>? finalErrors = null;

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var contractInstruction = attempt == 1
                    ? "Output MUST be valid JSON and match the role contract fields. Do not include markdown."
                    : $"Your previous output failed contract validation. Output ONLY valid JSON with required fields for persona {persona.Id}. No markdown, no prose outside JSON.";

                var request = new LanguageModelRequest(
                    PersonaId: persona.Id.ToString(),
                    PersonaPrompt: personaPrompt,
                    FlowPrompt: flowPrompt + "\n\n" + contractInstruction,
                    InputContext: inputContext
                );

                var response = provider.GenerateAsync(request, ct).GetAwaiter().GetResult();

                // Parse + validate
                List<string> errors;
                JsonElement? parsed = null;

                try
                {
                    using var doc = JsonDocument.Parse(response.OutputText);
                    parsed = doc.RootElement.Clone();
                    errors = RefineContracts.ValidatePersonaOutput(persona.Id.ToString(), parsed.Value);
                }
                catch (Exception ex)
                {
                    errors = new List<string> { $"Invalid JSON: {ex.Message}" };
                }

                attempts.Add(new
                {
                    attempt,
                    provider = provider.Name,
                    createdAtUtc = utc.ToString("O"),
                    outputText = response.OutputText,
                    validationErrors = errors
                });

                finalParsed = parsed;
                finalErrors = errors;

                if (errors.Count == 0 && parsed is not null)
                    break;
            }

            var succeeded = finalErrors is not null && finalErrors.Count == 0;

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
                    personaPromptFile = $"prompts/personas/{persona.PromptFileName}",
                    contractSchemaFile = $"schemas/refine/{persona.Id.ToString().ToLowerInvariant()}.schema.json"
                },
                attempts,
                result = new
                {
                    succeeded,
                    parsed = finalParsed,
                    validationErrors = finalErrors
                }
            };

            TurnWriter.WriteTurn(turnsDir, turnIndex, persona.Id.ToString(), payload);

            if (!succeeded)
            {
                RunWriter.WriteText(runDir, "summary.md",
                    $"# Refine Summary\n\nFAIL: Contract validation failed for persona {persona.Id} on turn {turnIndex}.\n\n" +
                    string.Join(Environment.NewLine, finalErrors ?? new List<string> { "Unknown validation failure." }));

                record.Status = "contract_failed";
                RunWriter.WriteJson(runDir, "run.json", record);
                return 7;
            }

            // Add pretty JSON into summary aggregation
            turnOutputs.Add((
                persona.Id.ToString(),
                JsonSerializer.Serialize(finalParsed, new JsonSerializerOptions { WriteIndented = true })
            ));

            turnIndex++;
        }

        // 10) Write summary
        RunWriter.WriteText(runDir, "summary.md", BuildSummaryMarkdown(runId, provider.Name, turnOutputs));

        // 11) Mark run complete
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
