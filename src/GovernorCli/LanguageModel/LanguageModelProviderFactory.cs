namespace GovernorCli.LanguageModel;

public static class LanguageModelProviderFactory
{
    private const string DefaultModel = "gpt-4o-mini";

    // Environment variables:
    // GOVERNOR_LM_PROVIDER = "stub" | "openai" (default: stub)
    // OPENAI_API_KEY = required when provider=openai
    // OPENAI_MODEL = default model name (default: gpt-4o-mini)
    // GOVERNOR_LLM_DEFAULT = default LLM model for all personas (default: gpt-4o-mini)
    public static ILanguageModelProvider Create(string? modelOverride = null)
    {
        var provider = (Environment.GetEnvironmentVariable("GOVERNOR_LM_PROVIDER") ?? "stub")
            .Trim().ToLowerInvariant();

        var model = modelOverride ?? 
            Environment.GetEnvironmentVariable("GOVERNOR_LLM_DEFAULT") ?? 
            DefaultModel;

        return provider switch
        {
            "openai" => CreateOpenAi(model),
            _ => new StubLanguageModelProvider()
        };
    }

    public static string GetDefaultModel()
    {
        return Environment.GetEnvironmentVariable("GOVERNOR_LLM_DEFAULT") ?? DefaultModel;
    }

    private static ILanguageModelProvider CreateOpenAi(string model)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OPENAI_API_KEY is required when GOVERNOR_LM_PROVIDER=openai.");

        return new OpenAiLanguageModelProvider(new HttpClient(), apiKey, model);
    }
}
