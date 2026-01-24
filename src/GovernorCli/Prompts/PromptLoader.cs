namespace GovernorCli.Prompts;

public static class PromptLoader
{
    public static string LoadPersonaPrompt(string workdir, string promptFileName)
    {
        var path = Path.Combine(workdir, "prompts", "personas", promptFileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Persona prompt file missing: {path}", path);

        return File.ReadAllText(path);
    }

    public static string LoadFlowPrompt(string workdir, string flowFileName)
    {
        var path = Path.Combine(workdir, "prompts", "flows", flowFileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Flow prompt file missing: {path}", path);

        return File.ReadAllText(path);
    }
}
