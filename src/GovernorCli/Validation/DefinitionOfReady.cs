namespace GovernorCli.Validation;

public static class DefinitionOfReady
{
    private static readonly HashSet<string> AllowedStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "candidate", "ready" };

    private static readonly HashSet<string> AllowedOwners =
        new(StringComparer.OrdinalIgnoreCase) { "PO", "SAD", "SASD", "QA", "MIBS" };

    private static readonly HashSet<string> AllowedSizes =
        new(StringComparer.OrdinalIgnoreCase) { "S", "M", "L" };

    public static List<string> Validate(GovernorCli.State.BacklogItem item)
    {
        var errors = new List<string>();

        if (item.Id <= 0)
            errors.Add("id must be a positive integer.");

        if (string.IsNullOrWhiteSpace(item.Title))
            errors.Add("title is required.");

        if (!AllowedStatuses.Contains(item.Status ?? ""))
            errors.Add("status must be 'candidate' or 'ready' to refine.");

        if (!AllowedOwners.Contains(item.Owner ?? ""))
            errors.Add("owner must be one of {PO,SAD,SASD,QA,MIBS}.");

        if (!AllowedSizes.Contains(item.Size ?? ""))
            errors.Add("size must be one of {S,M,L}.");

        return errors;
    }
}
