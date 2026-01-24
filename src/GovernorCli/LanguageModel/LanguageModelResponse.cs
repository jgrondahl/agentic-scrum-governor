namespace GovernorCli.LanguageModel
{
    public sealed record LanguageModelResponse(
    string PersonaId,
    string OutputText,
    Dictionary<string, string>? Metadata = null);
}
