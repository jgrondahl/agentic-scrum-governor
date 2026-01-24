namespace GovernorCli.Personas;
public static class PersonaCatalog
{
    public static readonly IReadOnlyList<Persona> RefinementOrder =
    [
        new(PersonaId.PO,   "Product Owner", "product-owner.md"),
        new(PersonaId.MIBS, "Music Industry Business Specialist", "music-biz-specialist.md"),
        new(PersonaId.SAD,  "Senior Architect Developer", "senior-architect-dev.md"),
        new(PersonaId.SASD, "Senior Audio Systems Developer", "senior-audio-dev.md"),
        new(PersonaId.QA,   "QA Engineer", "qa-engineer.md"),
    ];
}
