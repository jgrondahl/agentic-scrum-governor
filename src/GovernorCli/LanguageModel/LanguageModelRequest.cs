namespace GovernorCli.LanguageModel
{
    public sealed record LanguageModelRequest(
        string PersonaId,
        string PersonaPrompt,
        string FlowPrompt,
        string InputContext);
}