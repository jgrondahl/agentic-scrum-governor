using GovernorCli.Application.Models.Deliver;
using GovernorCli.Application.Stores;
using GovernorCli.Application.UseCases;
using GovernorCli.Domain.Enums;
using GovernorCli.Domain.Exceptions;

namespace GovernorCli.Flows;

/// <summary>
/// Orchestration layer for delivery flow.
/// Responsibility: Repo validation, preconditions, path construction, decision logging.
/// Delegates business logic to DeliverUseCase.
/// NO Infrastructure instantiation. All I/O via injected stores.
/// </summary>
public class DeliverFlow
{
    private readonly IDeliverUseCase _useCase;
    private readonly IBacklogStore _backlogStore;
    private readonly IEpicStore _epicStore;
    private readonly IWorkspaceStore _workspaceStore;
    private readonly IRunArtifactStore _runArtifactStore;
    private readonly IAppDeployer _appDeployer;
    private readonly IDecisionStore _decisionStore;

    public DeliverFlow(
        IDeliverUseCase useCase,
        IBacklogStore backlogStore,
        IEpicStore epicStore,
        IWorkspaceStore workspaceStore,
        IRunArtifactStore runArtifactStore,
        IAppDeployer appDeployer,
        IDecisionStore decisionStore)
    {
        _useCase = useCase;
        _backlogStore = backlogStore;
        _epicStore = epicStore;
        _workspaceStore = workspaceStore;
        _runArtifactStore = runArtifactStore;
        _appDeployer = appDeployer;
        _decisionStore = decisionStore;
    }

    /// <summary>
    /// Execute delivery flow with strict governance.
    /// Returns: Exit code for CLI.
    /// </summary>
    public FlowExitCode Execute(string workdir, int itemId, bool verbose, bool approve)
    {
        try
        {
            // 1) Validate layout (fail-fast, no mutations)
            var problems = RepoChecks.ValidateLayout(workdir);
            if (problems.Count > 0)
                throw new InvalidRepoLayoutException(problems);

            // 2) Paths (Flow responsibility)
            var backlogPath = Path.Combine(workdir, "state", "backlog.yaml");
            var runsDir = Path.Combine(workdir, "state", "runs");

            // 3) Load backlog (via injected store, not Infrastructure instantiation)
            var backlog = _backlogStore.Load(backlogPath);
            var item = backlog.Backlog.FirstOrDefault(x => x.Id == itemId)
                ?? throw new ItemNotFoundException(itemId);

            // 4) PRECONDITIONS (fail-fast, no workspace/workspace-app created until ALL pass)
            ValidateDeliverPreconditions(item, itemId, workdir);

            // 5) Resolve epic -> appId (already validated to exist)
            var appId = _epicStore.ResolveAppId(workdir, item.EpicId!);

            // 6) Orchestration values (Flow responsibility)
            var utc = DateTimeOffset.UtcNow;
            var runId = $"{utc:yyyyMMdd_HHmmss}_deliver_item-{itemId}";
            var approver = Environment.GetEnvironmentVariable("GOVERNOR_APPROVER") ?? "local";

            // 7) AFTER preconditions pass: reset and create workspace
            var workspaceRoot = _workspaceStore.ResetAndCreateWorkspace(workdir, appId);

            // 8) Create run directory for artifacts
            var runDir = _runArtifactStore.CreateRunFolder(runsDir, runId);

            // 9) Pass to UseCase (deterministic, zero environment coupling)
            var request = new DeliverRequest
            {
                ItemId = itemId,
                AppId = appId,
                BacklogPath = backlogPath,
                RunsDir = runsDir,
                Workdir = workdir,
                WorkspaceRoot = workspaceRoot,
                RunId = runId,
                RunDir = runDir,
                Approve = approve,
                TemplateId = item.DeliveryTemplateId!  // Already validated non-null
            };

            var response = _useCase.Process(request);

            // 10) STRICT APPROVAL GATE
            // - If validation failed, REFUSE EVERYTHING (no deploy, no decision)
            // - If approve=false, NEVER deploy or log
            // - If approve=true AND validation passed, deploy and log

            if (!response.ValidationPassed)
            {
                // Validation failed. Write preview artifacts already done by UseCase.
                // Return validation failed, but do NOT approve anything.
                return FlowExitCode.ValidationFailed;
            }

            // Validation passed. Check approval.
            if (approve)
            {
                // Deploy workspace to /apps/<appId>/ via AppDeployer
                var deployedFiles = _appDeployer.Deploy(workdir, workspaceRoot, appId);

                // Write patch.json (audit record)
                var patchApplied = new PatchApplied
                {
                    AppliedAtUtc = utc.ToString("O"),
                    ItemId = itemId,
                    AppId = appId,
                    RunId = runId,
                    RepoTarget = $"/apps/{appId}/",
                    FilesApplied = deployedFiles
                };
                _runArtifactStore.WriteJson(runDir, "patch.json", patchApplied);

                // Append decision log (ONLY AFTER successful deploy)
                _decisionStore.LogDecision(workdir,
                    $"{utc:O} | deliver approved | item={itemId} | run={runId} | by={approver}");

                return FlowExitCode.Success;
            }

            // approve=false, validation passed. Return success but do NOT deploy or log.
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
        catch (InvalidOperationException ex)
        {
            // Precondition failed (validation message already in exception)
            System.Diagnostics.Debug.WriteLine($"Precondition failed: {ex.Message}");
            return FlowExitCode.PreconditionFailed;
        }
        catch (Exception ex)
        {
            // Unexpected error
            System.Diagnostics.Debug.WriteLine($"Unexpected error: {ex}");
            return FlowExitCode.UnexpectedError;
        }
    }

    /// <summary>
    /// Validate all preconditions for delivery.
    /// Fail-fast: throw if ANY precondition fails.
    /// No workspace/app created until ALL preconditions pass.
    /// </summary>
    private void ValidateDeliverPreconditions(GovernorCli.State.BacklogItem item, int itemId, string workdir)
    {
        // 1) Status must be "ready_for_dev" (case-insensitive)
        if (!string.Equals(item.Status, "ready_for_dev", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Item {itemId}: status is '{item.Status}', expected 'ready_for_dev'");

        // 2) Estimate must exist and storyPoints >= 1
        if (item.Estimate is null || item.Estimate.StoryPoints < 1)
            throw new InvalidOperationException($"Item {itemId}: estimate missing or storyPoints < 1");

        // 3) epic_id must exist and non-empty
        if (string.IsNullOrEmpty(item.EpicId))
            throw new InvalidOperationException($"Item {itemId}: epic_id is required");

        // 4) delivery_template_id must exist and non-empty
        if (string.IsNullOrEmpty(item.DeliveryTemplateId))
            throw new InvalidOperationException($"Item {itemId}: delivery_template_id is required (must be set by Phase 2)");

        // 5) Epic must resolve to app_id in state/epics.yaml
        try
        {
            _epicStore.ResolveAppId(workdir, item.EpicId);
        }
        catch (KeyNotFoundException)
        {
            throw new InvalidOperationException($"Item {itemId}: epic_id '{item.EpicId}' not found in state/epics.yaml");
        }
        catch (FileNotFoundException)
        {
            throw new InvalidOperationException($"Item {itemId}: state/epics.yaml not found");
        }
    }
}
