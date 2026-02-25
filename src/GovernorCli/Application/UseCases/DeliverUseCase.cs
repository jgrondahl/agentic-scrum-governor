using GovernorCli.Application.Models;
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

        // Use ArchitecturePlan from request (loaded by Flow from Phase 2 artifacts)
        var phase2Plan = request.ArchitecturePlan;

        // Validate architecture provides what's needed for generation
        ValidateArchitecture(request, phase2Plan);

        // Build deliver-specific plan (simplified)
        var appTypeDescription = phase2Plan?.AppType ?? "unknown";
        var deliverPlan = new Application.Models.Deliver.DeliveryImplementationPlan
        {
            RunId = request.RunId,
            ItemId = request.ItemId,
            AppId = request.AppId,
            TemplateId = appTypeDescription,
            WorkspaceTargetPath = workspaceAppRoot,
            Actions = new()
            {
                $"Generate {appTypeDescription}",
                "Validate build",
                "Validate run"
            }
        };

        _runArtifactStore.WriteJson(runDir, "implementation-plan.json", deliverPlan);

        // 2) Generate candidate implementation (LLM or template)
        GenerateCandidate(request, workspaceAppRoot, runDir, phase2Plan, item);

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
            Plan = deliverPlan,
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

    private void ValidateArchitecture(DeliverRequest request, ImplementationPlan? plan)
    {
        if (plan == null)
            throw new InvalidOperationException(
                $"No implementation plan found. Run 'governor refine-tech --item {request.ItemId} --approve' first.");

        if (string.IsNullOrEmpty(plan.AppType))
            throw new InvalidOperationException(
                "Implementation plan is missing app_type. The SAD did not specify an application type. " +
                $"Please re-run 'governor refine-tech --item {request.ItemId}' and ensure the AI provides an app_type (e.g., web_blazor, web_api, console).");

        if (plan.Stack == null || string.IsNullOrEmpty(plan.Stack.Language))
            throw new InvalidOperationException(
                "Implementation plan is missing stack information (language, runtime, framework). " +
                $"Re-run 'governor refine-tech --item {request.ItemId}' to generate complete architecture.");
    }

    private void GenerateCandidate(
        DeliverRequest request,
        string workspaceAppRoot,
        string runDir,
        ImplementationPlan? phase2Plan,
        BacklogItem item)
    {
        // Check if we have LLM context for code generation
        var hasLlmContext = !string.IsNullOrEmpty(request.ArchitectureContent) ||
                           !string.IsNullOrEmpty(request.TechnicalTasksContent) ||
                           phase2Plan != null;

        if (hasLlmContext && phase2Plan != null)
        {
            GenerateWithLlmAsync(request, workspaceAppRoot, runDir, phase2Plan, item).GetAwaiter().GetResult();
            return;
        }

        // Fallback to fixture template only if no architecture plan
        if (phase2Plan == null)
        {
            Deliver.FixtureDotNetTemplateGenerator.Generate(workspaceAppRoot, request.AppId);
            return;
        }

        throw new InvalidOperationException(
        $"No code generation context available. " +
        $"Ensure 'governor refine-tech --item {request.ItemId}' has been run with approval.");
    }

    private async Task GenerateWithLlmAsync(
        DeliverRequest request,
        string workspaceAppRoot,
        string runDir,
        Models.ImplementationPlan? phase2Plan,
        BacklogItem item)
    {
        var modelConfig = request.GetModelConfig();
        var generator = new LlmCodeGenerator();

        var phases = new[] { "core", "tests", "config" };

        foreach (var phase in phases)
        {
            var phaseFileName = $"code-{phase}.json";
            var phaseFilePath = Path.Combine(runDir, phaseFileName);

            if (File.Exists(phaseFilePath))
            {
                // Resume: load existing generated files
                var existingJson = File.ReadAllText(phaseFilePath);
                var spec = System.Text.Json.JsonSerializer.Deserialize<CodeGenerationSpec>(existingJson);
                if (spec != null)
                {
                    WriteGeneratedFiles(spec, workspaceAppRoot);
                    continue;
                }
            }

            // Generate new - wrap in try-catch for detailed error messages
            var effectivePlan = phase2Plan ?? CreateDefaultPlan(request, item);
            try
            {
                await generator.GenerateCodeAsync(
                    effectivePlan,
                    item,
                    request.Workdir,
                    workspaceRoot: workspaceAppRoot,
                    runDir,
                    modelConfig,
                    phase,
                    request.ArchitectureContent,
                    request.QaPlanContent,
                    request.TechnicalTasksContent);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"LLM code generation FAILED during '{phase}' phase for item {item.Id}. " +
                    $"Error: {ex.Message}. " +
                    $"Run 'governor refine-tech --item {item.Id} --approve' to regenerate architecture, " +
                    $"or check that your LLM API key is configured.");
            }

            // Check if LLM returned error files (parse failures)
            var errorFile = Path.Combine(runDir, $"error-{phase}.txt");
            if (File.Exists(errorFile))
            {
                var errorContent = File.ReadAllText(errorFile);
                throw new InvalidOperationException(
                    $"LLM failed to parse response for '{phase}' phase (item {item.Id}). " +
                    $"The LLM returned invalid JSON. " +
                    $"Error details: {errorContent.Substring(0, Math.Min(500, errorContent.Length))}. " +
                    $"This may be a prompt issue - check prompts/flows/code-generation.md");
            }

            // Validate after core phase - fail fast if build fails
            if (phase == "core")
            {
                var validation = RunValidation(workspaceAppRoot, runDir);
                _runArtifactStore.WriteJson(runDir, $"validation-{phase}.json", validation);

                if (!validation.Passed)
                {
                    var buildLog = File.ReadAllText(Path.Combine(runDir, "build.stdout.log"));
                    var buildErr = File.ReadAllText(Path.Combine(runDir, "build.stderr.log"));

                    throw new InvalidOperationException(
                        $"Build validation FAILED during '{phase}' phase for item {item.Id}. " +
                        $"The generated code does not compile. " +
                        $"Build output: {buildLog.Substring(0, Math.Min(500, buildLog.Length))}. " +
                        $"Build errors: {buildErr.Substring(0, Math.Min(500, buildErr.Length))}. " +
                        $"This indicates the LLM generated invalid or incomplete code. " +
                        $"Re-run 'governor refine-tech --item {item.Id} --approve' to regenerate.");
                }
            }
        }

        // Verify at least one .csproj was generated
        var csprojFiles = Directory.GetFiles(workspaceAppRoot, "*.csproj", SearchOption.AllDirectories);
        if (csprojFiles.Length == 0)
        {
            var generatedFiles = Directory.Exists(workspaceAppRoot)
                ? string.Join(", ", Directory.GetFiles(workspaceAppRoot, "*", SearchOption.AllDirectories).Select(f => Path.GetFileName(f)))
                : "(none)";

            throw new InvalidOperationException(
                $"LLM code generation completed but no .csproj file was created for item {item.Id}. " +
                $"Generated files: {generatedFiles}. " +
                $"The 'config' phase may have failed to generate the project file. " +
                $"Check prompts/personas/senior-architect-dev.md includes instructions for .csproj generation.");
        }
    }

    private void WriteGeneratedFiles(CodeGenerationSpec spec, string workspaceRoot)
    {
        foreach (var file in spec.SourceFiles)
        {
            var fullPath = Path.Combine(workspaceRoot, file.Path);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, file.Content);
        }

        foreach (var file in spec.ConfigFiles)
        {
            var fullPath = Path.Combine(workspaceRoot, file.Path);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, file.Content);
        }

        foreach (var file in spec.TestFiles)
        {
            var fullPath = Path.Combine(workspaceRoot, file.Path);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, file.Content);
        }
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

    private Application.Models.ImplementationPlan CreateDefaultPlan(DeliverRequest request, BacklogItem item)
    {
        return new Application.Models.ImplementationPlan
        {
            PlanId = $"PLAN-{request.RunId}",
            CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            CreatedFromRunId = request.RunId,
            ItemId = request.ItemId,
            EpicId = request.EpicId ?? "",
            AppId = request.AppId,
            RepoTarget = $"apps/{request.AppId}",
            AppType = request.ArchitecturePlan?.AppType ?? "dotnet_console",
            Stack = new StackInfo
            {
                Language = "csharp",
                Runtime = "net8.0",
                Framework = "dotnet"
            },
            ProjectLayout = new List<ProjectFile>
            {
                new() { Path = "Program.cs", Kind = "source" }
            },
            BuildPlan = new List<ExecutionStep>
            {
                new() { Tool = "dotnet", Args = new List<string> { "build" }, Cwd = "." }
            },
            RunPlan = new List<ExecutionStep>
            {
                new() { Tool = "dotnet", Args = new List<string> { "run" }, Cwd = "." }
            },
            ValidationChecks = new List<ValidationCheck>
            {
                new() { Type = "exit_code_equals", Value = "0" }
            },
            PatchPolicy = new PatchPolicy
            {
                ExcludeGlobs = new List<string> { "bin/**", "obj/**" }
            }
        };
    }
}
