using GovernorCli.Personas;

namespace GovernorCli.LanguageModel;

public interface IPersonaLlmProvider
{
    string PersonaId { get; }
    string DisplayName { get; }
    string ModelUsed { get; }
    Task<LanguageModelResponse> GenerateAsync(LanguageModelRequest request, CancellationToken ct);
}

public static class PersonaLlmProviderFactory
{
    public static IPersonaLlmProvider Create(PersonaId personaId, PersonaModelConfig? config = null)
    {
        config ??= PersonaModelConfig.FromEnvironment();
        
        var model = config.GetModel(personaId);
        var baseProvider = LanguageModelProviderFactory.Create(model);

        return personaId switch
        {
            PersonaId.SAD => new PersonaLlmProvider("SAD", "Senior Architect Developer", model, baseProvider),
            PersonaId.SASD => new PersonaLlmProvider("SASD", "Senior Audio Systems Developer", model, baseProvider),
            PersonaId.QA => new PersonaLlmProvider("QA", "QA Engineer", model, baseProvider),
            _ => throw new ArgumentException($"No provider configured for persona: {personaId}")
        };
    }

    private sealed class PersonaLlmProvider : IPersonaLlmProvider
    {
        private readonly ILanguageModelProvider _inner;

        public PersonaLlmProvider(string personaId, string displayName, string modelUsed, ILanguageModelProvider inner)
        {
            PersonaId = personaId;
            DisplayName = displayName;
            ModelUsed = modelUsed;
            _inner = inner;
        }

        public string PersonaId { get; }
        public string DisplayName { get; }
        public string ModelUsed { get; }

        public Task<LanguageModelResponse> GenerateAsync(LanguageModelRequest request, CancellationToken ct)
            => _inner.GenerateAsync(request, ct);
    }
}
