using GovernorCli.Application.Models.Deliver;
using GovernorCli.Application.Stores;
using GovernorCli.Domain.Enums;
using GovernorCli.Domain.Exceptions;
using GovernorCli.State;

namespace GovernorCli.Application.UseCases;

/// <summary>
/// Business logic for delivery orchestration.
/// Responsibility: Deterministic execution, artifact generation, validation.
/// Zero responsibility: File I/O (via stores), CLI output, orchestration decisions (via Flow).
/// </summary>
public class DeliverUseCase : IDeliverUseCase
{
    private readonly IBacklogStore _backlogStore;
    private readonly IRunArtifactStore _runArtifactStore;
    private readonly IProcessRunner _processRunner;
    private readonly IAppDeployer _appDeployer;

    public DeliverUseCase(
        IBacklogStore backlogStore,
        IRunArtifactStore runArtifactStore,
        IProcessRunner processRunner,
        IAppDeployer appDeployer)
    {
        _backlogStore = backlogStore;
        _runArtifactStore = runArtifactStore;
        _processRunner = processRunner;
        _appDeployer = appDeployer;
    }

    /// <summary>
    /// Process delivery request: generate, validate, optionally deploy.
    /// </summary>
    public DeliverResponse Process(DeliverRequest request)
    {
        var utc = DateTimeOffset.UtcNow;

        // Load backlog (via store, abstract)
        var backlog = _backlogStore.Load(request.BacklogPath);

        // Validate item exists
        var item = backlog.Backlog.FirstOrDefault(x => x.Id == request.ItemId)
            ?? throw new ItemNotFoundException(request.ItemId);

        // Create run directory
        var runDir = _runArtifactStore.CreateRunFolder(request.RunsDir, request.RunId);

        // 1) Generate implementation plan
        var workspaceAppRoot = Path.Combine(request.WorkspaceRoot, "apps", request.AppId);

        // Select template by ID (allows future non-fixture generators)
        ValidateTemplate(request.TemplateId);

        var plan = new ImplementationPlan
        {
            RunId = request.RunId,
            ItemId = request.ItemId,
            AppId = request.AppId,
            TemplateId = request.TemplateId,  // From Phase 2
            WorkspaceTargetPath = workspaceAppRoot,
            Actions = new()
            {
                $"Generate {request.TemplateId}",
                "Validate build",
                "Validate run"
            }
        };

        _runArtifactStore.WriteJson(runDir, "implementation-plan.json", plan);

        // 2) Generate candidate implementation (template-specific)
        GenerateCandidate(request.TemplateId, workspaceAppRoot, request.AppId);

        // 3) Validate candidate: run build and run commands
        var validation = RunValidation(workspaceAppRoot, runDir);
        _runArtifactStore.WriteJson(runDir, "validation.json", validation);

        // 4) Compute patch preview (read-only)
        var preview = ComputePatchPreview(request.AppId, workspaceAppRoot, validation.Passed, utc);
        preview.ItemId = request.ItemId;  // Set ItemId in the typed model
        _runArtifactStore.WriteJson(runDir, "patch.preview.json", preview);

        // Simple preview diff: list files
        var previewDiff = string.Join("\n", preview.Files.Select(f => $"+ {f}"));
        _runArtifactStore.WriteText(runDir, "patch.preview.diff", previewDiff);

        var result = new DeliverResult
        {
            Plan = plan,
            Validation = validation,
            Preview = preview,
            WorkspaceRoot = request.WorkspaceRoot,
            WorkspaceAppRoot = workspaceAppRoot,
            RunDir = runDir
        };

        // 5) If not approved or validation failed, done (preview only)
        if (!request.Approve || !validation.Passed)
        {
            WriteSummary(runDir, request.ItemId, request.RunId, preview, validation.Passed);
            return new DeliverResponse
            {
                Success = validation.Passed,
                ValidationPassed = validation.Passed,
                Result = result
            };
        }

        // 6) Deploy: copy workspace to /apps/{appId}/
        var deployed = _appDeployer.Deploy(request.Workdir, request.WorkspaceRoot, request.AppId);
        var appliedPatch = new PatchApplied
        {
            AppliedAtUtc = utc.ToString("O"),
            ItemId = request.ItemId,
            AppId = request.AppId,
            RunId = request.RunId,
            RepoTarget = $"/apps/{request.AppId}/",
            FilesApplied = deployed
        };

        result.PatchApplied = appliedPatch;
        _runArtifactStore.WriteJson(runDir, "patch.json", appliedPatch);

        WriteSummary(runDir, request.ItemId, request.RunId, preview, validation.Passed, approved: true);

        return new DeliverResponse
        {
            Success = true,
            ValidationPassed = validation.Passed,
            Result = result
        };
    }

    private ValidationReport RunValidation(string appRoot, string runDir)
    {
        var commands = new List<ValidationCommandResult>();
        var allPassed = true;

        // 1) dotnet build
        var buildStdout = Path.Combine(runDir, "build.stdout.log");
        var buildStderr = Path.Combine(runDir, "build.stderr.log");
        var buildExitCode = _processRunner.Run(
            AllowedProcess.DotnetBuild,
            appRoot,
            Array.Empty<string>(),
            buildStdout,
            buildStderr);

        var buildResult = new ValidationCommandResult
        {
            Name = "dotnet build",
            WorkingDir = appRoot,
            CommandLine = "dotnet build",
            ExitCode = buildExitCode,
            StdoutFile = buildStdout,
            StderrFile = buildStderr
        };
        commands.Add(buildResult);
        if (buildExitCode != 0)
            allPassed = false;

        // 2) dotnet run
        var runStdout = Path.Combine(runDir, "run.stdout.log");
        var runStderr = Path.Combine(runDir, "run.stderr.log");
        var runExitCode = _processRunner.Run(
            AllowedProcess.DotnetRun,
            appRoot,
            Array.Empty<string>(),
            runStdout,
            runStderr);

        var runResult = new ValidationCommandResult
        {
            Name = "dotnet run",
            WorkingDir = appRoot,
            CommandLine = "dotnet run",
            ExitCode = runExitCode,
            StdoutFile = runStdout,
            StderrFile = runStderr
        };
        commands.Add(runResult);
        if (runExitCode != 0)
            allPassed = false;

        return new ValidationReport
        {
            Passed = allPassed,
            Commands = commands
        };
    }

    private DeliverPatchPreview ComputePatchPreview(string appId, string workspaceAppRoot, bool validationPassed, DateTimeOffset utc)
    {
        var patchFiles = new List<PatchFile>();
        if (Directory.Exists(workspaceAppRoot))
        {
            var di = new DirectoryInfo(workspaceAppRoot);
            CollectPatchFiles(di, appId, patchFiles);
        }

        return new DeliverPatchPreview
        {
            ComputedAtUtc = utc.ToString("O"),
            AppId = appId,
            RepoTarget = $"/apps/{appId}/",
            Files = patchFiles,
            ValidationPassed = validationPassed
        };
    }

    private void CollectPatchFiles(DirectoryInfo dir, string appIdPrefix, List<PatchFile> files)
    {
        foreach (var file in dir.GetFiles())
        {
            var relativePath = Path.Combine(appIdPrefix, file.Name);
            var sha256 = ComputeFileSha256(file.FullName);

            files.Add(new PatchFile
            {
                Action = "A",  // Add (new file in workspace)
                Path = relativePath,
                Size = file.Length,
                WorkspaceSha256 = sha256,
                RepoSha256 = null  // Not computed for preview (only on deploy)
            });
        }

        foreach (var subDir in dir.GetDirectories())
            CollectPatchFiles(subDir, Path.Combine(appIdPrefix, subDir.Name), files);
    }

    private string ComputeFileSha256(string filePath)
    {
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            using (var stream = File.OpenRead(filePath))
            {
                var hash = sha256.ComputeHash(stream);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
        }
    }

    private void ValidateTemplate(string templateId)
    {
        // Allowlist of valid templates (Phase 2 outputs must use one of these)
        var allowedTemplates = new[] { Deliver.FixtureDotNetTemplateGenerator.TemplateId };
        if (!allowedTemplates.Contains(templateId))
            throw new InvalidOperationException($"Template not allowed: {templateId}. Must be one of: {string.Join(", ", allowedTemplates)}");
    }

    private void GenerateCandidate(string templateId, string workspaceAppRoot, string appId)
    {
        // Route to correct generator (currently only fixture, extensible for Phase 4+)
        if (templateId == Deliver.FixtureDotNetTemplateGenerator.TemplateId)
        {
            Deliver.FixtureDotNetTemplateGenerator.Generate(workspaceAppRoot, appId);
            return;
        }

        throw new InvalidOperationException($"No generator found for template: {templateId}");
    }

    private void WriteSummary(string runDir, int itemId, string runId, DeliverPatchPreview preview, bool validationPassed, bool approved = false)
    {
        var status = approved ? "✓ APPROVED and DEPLOYED" : (validationPassed ? "✓ VALIDATED (preview)" : "✗ VALIDATION FAILED");
        var summary = $"""
# Deliver Summary

Item: {itemId}
Run: {runId}
Status: {status}

## Validation
- Passed: {validationPassed}
- Commands: 2 (dotnet build, dotnet run)

## Patch Preview
- Files: {preview.Files.Count} files
- Target: {preview.RepoTarget}

## Next Steps
{(approved ? "✓ Patch applied to repo. Decision logged." : (validationPassed ? "Next: Approve deployment with:\n  governor deliver --item " + itemId + " --approve" : "Fix issues and retry."))}
""";
        _runArtifactStore.WriteText(runDir, "summary.md", summary);
    }
}
