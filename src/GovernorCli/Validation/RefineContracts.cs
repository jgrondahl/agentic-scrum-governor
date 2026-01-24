using System.Text.Json;

namespace GovernorCli.Validation;

public static class RefineContracts
{
    public static List<string> ValidatePersonaOutput(string personaId, JsonElement root)
    {
        var errors = new List<string>();

        // Common fields
        RequireStringArray(root, "risks", errors);
        RequireStringArray(root, "assumptions", errors);
        RequireStringArray(root, "recommendations", errors);

        switch (personaId)
        {
            case "PO":
                RequireStringArray(root, "acceptanceCriteriaUpdates", errors);
                RequireStringArray(root, "nonGoalsUpdates", errors);
                RequireInt(root, "prioritySuggestion", min: 1, errors);
                break;

            case "MIBS":
                RequireString(root, "icp", errors);
                RequireString(root, "positioning", errors);
                RequireString(root, "pricingHypothesis", errors);
                RequireStringArray(root, "scopeTraps", errors);
                break;

            case "SAD":
                RequireStringArray(root, "architectureChanges", errors);
                RequireStringArray(root, "interfaceNotes", errors);
                RequireStringArray(root, "nfrs", errors);
                break;

            case "SASD":
                RequireString(root, "dspApproach", errors);
                RequireStringArray(root, "metrics", errors);
                RequireStringArray(root, "constraints", errors);
                break;

            case "QA":
                RequireStringArray(root, "testOracles", errors);
                RequireStringArray(root, "edgeCases", errors);
                RequireStringArray(root, "dodChecklist", errors);
                break;

            default:
                errors.Add($"Unknown personaId '{personaId}' for contract validation.");
                break;
        }

        return errors;
    }

    private static void RequireString(JsonElement root, string property, List<string> errors)
    {
        if (!root.TryGetProperty(property, out var v) || v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()))
            errors.Add($"Missing/invalid '{property}' (required non-empty string).");
    }

    private static void RequireInt(JsonElement root, string property, int min, List<string> errors)
    {
        if (!root.TryGetProperty(property, out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var i) || i < min)
            errors.Add($"Missing/invalid '{property}' (required integer >= {min}).");
    }

    private static void RequireStringArray(JsonElement root, string property, List<string> errors)
    {
        if (!root.TryGetProperty(property, out var v) || v.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"Missing/invalid '{property}' (required array of strings).");
            return;
        }

        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                errors.Add($"Invalid '{property}' entry (all items must be strings).");
                return;
            }
        }
    }
}
