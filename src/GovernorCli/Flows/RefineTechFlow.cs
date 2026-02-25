using GovernorCli.Application.Stores;
using GovernorCli.Application.UseCases;
using GovernorCli.Domain.Enums;
using GovernorCli.Domain.Exceptions;

namespace GovernorCli.Flows;

public class RefineTechFlow
{
    private readonly RefineTechUseCase _useCase;
    private readonly IDecisionStore _decisionStore;

    public RefineTechFlow(RefineTechUseCase useCase, IDecisionStore decisionStore)
    {
        _useCase = useCase;
        _decisionStore = decisionStore;
    }

    /// <summary>
    /// Execute technical readiness review flow.
    /// Orchestrates: validation, useCase invocation, decision logging.
    /// Returns: Exit code for CLI.
    /// </summary>
    public FlowExitCode Execute(
        string workdir, 
        int itemId, 
        bool verbose, 
        bool approve,
        bool useSameModel = false,
        string? modelSad = null,
        string? modelSasd = null,
        string? modelQa = null)
    {
        try
        {
            // Validate layout
            var problems = RepoChecks.ValidateLayout(workdir);
            if (problems.Count > 0)
                throw new InvalidRepoLayoutException(problems);

            // Paths (Flow responsibility)
            var backlogPath = Path.Combine(workdir, "state", "backlog.yaml");
            var runsDir = Path.Combine(workdir, "state", "runs");

            // Orchestration values (Flow responsibility)
            var utc = DateTimeOffset.UtcNow;
            var runId = $"{utc:yyyyMMdd_HHmmss}_refine-tech_item-{itemId}";
            var approver = Environment.GetEnvironmentVariable("GOVERNOR_APPROVER") ?? "local";

            // Pass to UseCase (UseCase has zero environment coupling)
            var request = new RefineTechRequest
            {
                ItemId = itemId,
                BacklogPath = backlogPath,
                RunsDir = runsDir,
                Workdir = workdir,
                RunId = runId,
                Approve = approve,
                UseSameModel = useSameModel,
                ModelSad = modelSad,
                ModelSasd = modelSasd,
                ModelQa = modelQa
            };

            var result = _useCase.Process(request);

            // Flow logs decision (after successful approval)
            if (approve && result.Success)
            {
                _decisionStore.LogDecision(workdir,
                    $"{utc:O} | refine-tech approved | item={itemId} | run={runId} | by={approver}");
            }

            return FlowExitCode.Success;
        }
        catch (InvalidRepoLayoutException)
        {
            return FlowExitCode.InvalidRepoLayout;
        }
        catch (ItemNotFoundException)
        {
            return FlowExitCode.ItemNotFound;
        }
        catch (BacklogParseException)
        {
            return FlowExitCode.BacklogParseError;
        }
        catch (Exception)
        {
            return FlowExitCode.ApplyFailed;
        }
    }
}
