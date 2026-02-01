namespace GovernorCli.LanguageModel;

public static class LanguageModelProviderFactory
{
    // Environment variables:
    // GOVERNOR_LM_PROVIDER = "stub" | "openai" (default: stub)
    // OPENAI_API_KEY = required when provider=openai
    // OPENAI_MODEL = default model name (default: gpt-5)
    public static ILanguageModelProvider Create()
    {
        var provider = (Environment.GetEnvironmentVariable("GOVERNOR_LM_PROVIDER") ?? "stub")
            .Trim().ToLowerInvariant();

        return provider switch
        {
            "openai" => CreateOpenAi(),
            _ => new StubLanguageModelProvider()
        };
    }

    private static ILanguageModelProvider CreateOpenAi()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OPENAI_API_KEY is required when GOVERNOR_LM_PROVIDER=openai.");

        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5";
        return new OpenAiLanguageModelProvider(new HttpClient(), apiKey, model);
    }
}
