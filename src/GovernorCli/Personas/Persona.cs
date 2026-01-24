namespace GovernorCli.Personas;

public sealed record Persona(
    PersonaId Id,
    string DisplayName,
    string PromptFileName
);
