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
    // 8 apply/update backlog failed (new)
    public static int Run(string workdir, int itemId, bool verbose, bool approve)
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

        // 8) Provider (real or stub)
        ILanguageModelProvider provider = LanguageModelProviderFactory.Create();
        var turnsDir = TurnWriter.EnsureTurnsDir(runDir);

        // Capture PO parsed output for application step
        JsonElement? poParsed = null;

        // For summary.md
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

            var attempts = new List<object>();
            JsonElement? finalParsed = null;
            List<string>? finalErrors = null;

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var requiredKeys = persona.Id.ToString() switch
                {
                    "PO" => @"risks, assumptions, recommendations, acceptanceCriteriaUpdates, nonGoalsUpdates, prioritySuggestion",
                    "MIBS" => @"risks, assumptions, recommendations, icp, positioning, pricingHypothesis, scopeTraps",
                    "SAD" => @"risks, assumptions, recommendations, architectureChanges, interfaceNotes, nfrs",
                    "SASD" => @"risks, assumptions, recommendations, dspApproach, metrics, constraints",
                    "QA" => @"risks, assumptions, recommendations, testOracles, edgeCases, dodChecklist",
                    _ => @"risks, assumptions, recommendations"
                };

                var contractInstruction =
                $"""
                OUTPUT RULES (NON-NEGOTIABLE):
                - Output MUST be a SINGLE JSON object.
                - Do NOT wrap in markdown.
                - Do NOT include any prose before or after the JSON.
                - All required keys MUST exist: {requiredKeys}
                - All array fields must be JSON arrays of strings.
                - prioritySuggestion must be an integer >= 1 (PO only).

                If you are unsure about content, use placeholder strings, but do NOT omit fields.
                """;

                var request = new LanguageModelRequest(
                    PersonaId: persona.Id.ToString(),
                    PersonaPrompt: personaPrompt,
                    FlowPrompt: flowPrompt + "\n\n" + contractInstruction,
                    InputContext: inputContext
                );

                var response = provider.GenerateAsync(request, ct).GetAwaiter().GetResult();

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

            // Capture PO output for backlog update
            if (persona.Id.ToString() == "PO" && finalParsed is not null)
                poParsed = finalParsed;

            // Include pretty JSON in summary aggregation
            turnOutputs.Add((
                persona.Id.ToString(),
                JsonSerializer.Serialize(finalParsed, new JsonSerializerOptions { WriteIndented = true })
            ));

            turnIndex++;
        }

        // 10) Apply PO output to backlog + write patch.json
        try
        {
            if (poParsed is not null)
            {
                // Always compute + write preview
                var patch = ComputePoPatch(backlog, itemId, poParsed.Value);

                var preview = new
                {
                    computedAtUtc = patch.ComputedAtUtc.ToString("O"),
                    itemId = patch.ItemId,
                    changes = new
                    {
                        priority = new { before = patch.BeforePriority, after = patch.AfterPriority },
                        status = new { before = patch.BeforeStatus, after = patch.AfterStatus },
                        acceptance_criteria = new
                        {
                            before = patch.BeforeAcceptanceCriteria,
                            after = patch.AfterAcceptanceCriteria,
                            added = patch.AddedAcceptanceCriteria
                        },
                        non_goals = new
                        {
                            before = patch.BeforeNonGoals,
                            after = patch.AfterNonGoals,
                            added = patch.AddedNonGoals
                        }
                    }
                };

                RunWriter.WriteJson(runDir, "patch.preview.json", preview);

                if (approve)
                {
                    ApplyPoPatchAndPersist(backlogPath, backlog, patch);
                    var approver = Environment.GetEnvironmentVariable("GOVERNOR_APPROVER") ?? "local";
                    DecisionLog.Append(workdir, $"{DateTimeOffset.UtcNow:O} | refine approved | item={itemId} | run={runId} | by={approver}");
                    RunWriter.WriteJson(runDir, "patch.json", preview);
                }
            }
        }
        catch (Exception ex)
        {
            RunWriter.WriteText(runDir, "summary.md",
                $"# Refine Summary\n\nFAIL: Could not compute/apply PO patch.\n\n{ex.Message}");

            record.Status = "apply_failed";
            RunWriter.WriteJson(runDir, "run.json", record);
            return 8;
        }


        // 11) Write summary
        RunWriter.WriteText(runDir, "summary.md", BuildSummaryMarkdown(runId, provider.Name, turnOutputs));

        // 12) Mark run complete
        record.Status = "completed";
        RunWriter.WriteJson(runDir, "run.json", record);

        return 0;
    }

    private static void ApplyPoRefinementAndPersist(
        string workdir,
        string runDir,
        string backlogPath,
        BacklogFile backlog,
        int itemId,
        JsonElement poParsed)
    {
        // Deserialize PO contract from parsed JSON
        var po = JsonSerializer.Deserialize<PoRefineContract>(
                    poParsed.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                 ?? throw new InvalidOperationException("PO parsed output could not be deserialized.");

        if (po.PrioritySuggestion < 1)
            throw new InvalidOperationException($"Invalid PrioritySuggestion from PO output: {po.PrioritySuggestion}. Must be >= 1.");


        var item = backlog.Backlog.First(x => x.Id == itemId);

        static HashSet<string> ToSet(IEnumerable<string> xs) =>
            new(xs.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);

        // Snapshot "before" for patch
        var before = new
        {
            status = item.Status,
            priority = item.Priority,
            acceptance_criteria = item.Acceptance_Criteria.ToArray(),
            non_goals = item.Non_Goals.ToArray()
        };

        // Update priority if suggested
        item.Priority = po.PrioritySuggestion;

        // Optional: when refined successfully, mark ready
        if (string.Equals(item.Status, "candidate", StringComparison.OrdinalIgnoreCase))
            item.Status = "ready";

        // Persist backlog file
        BacklogSaver.Save(backlogPath, backlog);

        var after = new
        {
            status = item.Status,
            priority = item.Priority,
            acceptance_criteria = item.Acceptance_Criteria.ToArray(),
            non_goals = item.Non_Goals.ToArray()
        };

        var beforeAc = ToSet(before.acceptance_criteria);
        var afterAc = ToSet(after.acceptance_criteria);
        afterAc.ExceptWith(beforeAc);
        var addedAcceptanceCriteria = afterAc.ToArray();

        var beforeNg = ToSet(before.non_goals);
        var afterNg = ToSet(after.non_goals);
        afterNg.ExceptWith(beforeNg);
        var addedNonGoals = afterNg.ToArray();

        const int MaxAdds = 3;

        if (addedAcceptanceCriteria.Length > MaxAdds)
            addedAcceptanceCriteria = addedAcceptanceCriteria.Take(MaxAdds).ToArray();

        if (addedNonGoals.Length > MaxAdds)
            addedNonGoals = addedNonGoals.Take(MaxAdds).ToArray();

        // Merge uniquely (don’t destroy existing content)
        item.Acceptance_Criteria = MergeUnique(item.Acceptance_Criteria, addedAcceptanceCriteria.ToList());
        item.Non_Goals = MergeUnique(item.Non_Goals, addedNonGoals.ToList());

        var patch = new
        {
            appliedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            itemId,
            changes = new
            {
                priority = new { before = before.priority, after = after.priority },
                status = new { before = before.status, after = after.status },

                acceptance_criteria = new
                {
                    before = before.acceptance_criteria,
                    after = after.acceptance_criteria,
                    added = addedAcceptanceCriteria
                },
                non_goals = new
                {
                    before = before.non_goals,
                    after = after.non_goals,
                    added = addedNonGoals
                }
            }
        };

        RunWriter.WriteJson(runDir, "patch.json", patch);
    }

    private static PoPatch ComputePoPatch(
    BacklogFile backlog,
    int itemId,
    JsonElement poParsed)
    {
        var po = JsonSerializer.Deserialize<PoRefineContract>(
                    poParsed.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                 ?? throw new InvalidOperationException("PO parsed output could not be deserialized.");

        if (po.PrioritySuggestion < 1)
            throw new InvalidOperationException($"Invalid PrioritySuggestion from PO output: {po.PrioritySuggestion}. Must be >= 1.");

        // Find item (we will compute on a clone so we don't mutate the real backlog yet)
        var original = backlog.Backlog.First(x => x.Id == itemId);

        // Capture BEFORE (truthful)
        var beforeStatus = original.Status;
        var beforePriority = original.Priority;
        var beforeAc = original.Acceptance_Criteria.ToArray();
        var beforeNg = original.Non_Goals.ToArray();

        // Compute AFTER by applying changes to local copies only
        var afterStatus = original.Status;
        var afterPriority = po.PrioritySuggestion;

        // Merge uniquely (same semantics you already use)
        var mergedAc = MergeUnique(original.Acceptance_Criteria, po.AcceptanceCriteriaUpdates);
        var mergedNg = MergeUnique(original.Non_Goals, po.NonGoalsUpdates);

        // Delta sets
        static HashSet<string> ToSet(IEnumerable<string> xs) =>
            new(xs.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);

        var addedAc = ToSet(mergedAc);
        addedAc.ExceptWith(ToSet(beforeAc));
        var addedNg = ToSet(mergedNg);
        addedNg.ExceptWith(ToSet(beforeNg));

        // Anti-churn cap (keep your MaxAdds rule)
        const int MaxAdds = 3;
        var addedAcArr = addedAc.Take(MaxAdds).ToArray();
        var addedNgArr = addedNg.Take(MaxAdds).ToArray();

        // Now compute final AFTER arrays that correspond to what we'd actually apply
        // (Only apply up to MaxAdds new entries per run)
        var finalAfterAc = MergeUnique(original.Acceptance_Criteria, addedAcArr.ToList()).ToArray();
        var finalAfterNg = MergeUnique(original.Non_Goals, addedNgArr.ToList()).ToArray();

        return new PoPatch
        {
            ComputedAtUtc = DateTimeOffset.UtcNow,
            ItemId = itemId,

            BeforeStatus = beforeStatus,
            AfterStatus = afterStatus,

            BeforePriority = beforePriority,
            AfterPriority = afterPriority,

            BeforeAcceptanceCriteria = beforeAc,
            AfterAcceptanceCriteria = finalAfterAc,
            AddedAcceptanceCriteria = addedAcArr,

            BeforeNonGoals = beforeNg,
            AfterNonGoals = finalAfterNg,
            AddedNonGoals = addedNgArr
        };
    }

    private static void ApplyPoPatchAndPersist(
    string backlogPath,
    BacklogFile backlog,
    PoPatch patch)
    {
        var item = backlog.Backlog.First(x => x.Id == patch.ItemId);

        // Apply exact "after" state from the patch (deterministic)
        item.Status = patch.AfterStatus;
        item.Priority = patch.AfterPriority;

        item.Acceptance_Criteria = patch.AfterAcceptanceCriteria.ToList();
        item.Non_Goals = patch.AfterNonGoals.ToList();

        BacklogSaver.Save(backlogPath, backlog);
    }

    private static List<string> MergeUnique(List<string> existing, List<string> updates)
    {
        var set = new HashSet<string>(existing.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var u in updates.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()))
            set.Add(u);

        return set.ToList();
    }
    private sealed class PoPatch
    {
        public DateTimeOffset ComputedAtUtc { get; init; }
        public int ItemId { get; init; }

        public string BeforeStatus { get; init; } = "";
        public string AfterStatus { get; init; } = "";

        public int BeforePriority { get; init; }
        public int AfterPriority { get; init; }

        public string[] BeforeAcceptanceCriteria { get; init; } = Array.Empty<string>();
        public string[] AfterAcceptanceCriteria { get; init; } = Array.Empty<string>();
        public string[] AddedAcceptanceCriteria { get; init; } = Array.Empty<string>();

        public string[] BeforeNonGoals { get; init; } = Array.Empty<string>();
        public string[] AfterNonGoals { get; init; } = Array.Empty<string>();
        public string[] AddedNonGoals { get; init; } = Array.Empty<string>();
    }

    private sealed class PoRefineContract
    {
        public List<string> Risks { get; set; } = new();
        public List<string> Assumptions { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();

        public List<string> AcceptanceCriteriaUpdates { get; set; } = new();
        public List<string> NonGoalsUpdates { get; set; } = new();
        public int PrioritySuggestion { get; set; }
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
