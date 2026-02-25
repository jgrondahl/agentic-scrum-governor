using GovernorCli.Personas;

namespace GovernorCli.LanguageModel;

public class PersonaModelConfig
{
    public string? SadModel { get; set; }
    public string? SasdModel { get; set; }
    public string? QaModel { get; set; }
    public string DefaultModel { get; set; } = "gpt-4o-mini";

    public static PersonaModelConfig FromEnvironment()
    {
        return new PersonaModelConfig
        {
            SadModel = Environment.GetEnvironmentVariable("GOVERNOR_LLM_SAD"),
            SasdModel = Environment.GetEnvironmentVariable("GOVERNOR_LLM_SASD"),
            QaModel = Environment.GetEnvironmentVariable("GOVERNOR_LLM_QA"),
            DefaultModel = Environment.GetEnvironmentVariable("GOVERNOR_LLM_DEFAULT") ?? "gpt-4o-mini"
        };
    }

    public string GetModel(PersonaId persona)
    {
        return persona switch
        {
            PersonaId.SAD => SadModel ?? DefaultModel,
            PersonaId.SASD => SasdModel ?? DefaultModel,
            PersonaId.QA => QaModel ?? DefaultModel,
            _ => DefaultModel
        };
    }

    public PersonaModelConfig WithOverride(PersonaId persona, string model)
    {
        var copy = new PersonaModelConfig
        {
            SadModel = SadModel,
            SasdModel = SasdModel,
            QaModel = QaModel,
            DefaultModel = DefaultModel
        };

        switch (persona)
        {
            case PersonaId.SAD:
                copy.SadModel = model;
                break;
            case PersonaId.SASD:
                copy.SasdModel = model;
                break;
            case PersonaId.QA:
                copy.QaModel = model;
                break;
        }

        return copy;
    }

    public PersonaModelConfig WithSameModel(string model)
    {
        return new PersonaModelConfig
        {
            SadModel = model,
            SasdModel = model,
            QaModel = model,
            DefaultModel = model
        };
    }
}
