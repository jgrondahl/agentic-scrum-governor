using GovernorCli.Application.Models.Deliver;
using GovernorCli.LanguageModel;
using GovernorCli.Personas;

namespace GovernorCli.Application.UseCases;

/// <summary>
/// Request for delivery operation.
/// All decision context comes from Flow (approver, runId, paths).
/// ArchitecturePlan comes from Phase 2 (refine-tech) - contains app_type, stack, projects.
/// UseCase does not read environment or format decisions.
/// </summary>
public class DeliverRequest
{
    public int ItemId { get; set; }
    public string AppId { get; set; } = "";
    public string BacklogPath { get; set; } = "";
    public string RunsDir { get; set; } = "";
    public string RunDir { get; set; } = "";       // Created by Flow
    public string Workdir { get; set; } = "";
    public string WorkspaceRoot { get; set; } = "";
    public string RunId { get; set; } = "";
    public bool Approve { get; set; }

    // Architecture from Phase 2 (refine-tech)
    public Application.Models.ImplementationPlan? ArchitecturePlan { get; set; }

    // Design artifacts for LLM code generation
    public string? ArchitectureContent { get; set; }
    public string? QaPlanContent { get; set; }
    public string? TechnicalTasksContent { get; set; }
    public string? EpicId { get; set; }

    // Model configuration
    public bool UseSameModel { get; set; }
    public string? ModelSad { get; set; }
    public string? ModelSasd { get; set; }
    public string? ModelQa { get; set; }

    public PersonaModelConfig GetModelConfig()
    {
        var config = PersonaModelConfig.FromEnvironment();

        if (UseSameModel)
        {
            var model = ModelSad ?? ModelSasd ?? ModelQa ?? config.DefaultModel;
            return config.WithSameModel(model);
        }

        if (!string.IsNullOrEmpty(ModelSad))
            config = config.WithOverride(PersonaId.SAD, ModelSad);
        if (!string.IsNullOrEmpty(ModelSasd))
            config = config.WithOverride(PersonaId.SASD, ModelSasd);
        if (!string.IsNullOrEmpty(ModelQa))
            config = config.WithOverride(PersonaId.QA, ModelQa);

        return config;
    }
}

/// <summary>
/// Result of delivery operation.
/// Contains all artifacts and outcome.
/// </summary>
public class DeliverResponse
{
    public bool Success { get; set; }
    public bool ValidationPassed { get; set; }
    public DeliverResult Result { get; set; } = new();
}
