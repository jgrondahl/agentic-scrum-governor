namespace GovernorCli.LanguageModel
{
    public interface ILanguageModelProvider
    {
        string Name { get; }
        Task<LanguageModelResponse> GenerateAsync(LanguageModelRequest request, CancellationToken ct);
    }
}
