using GovernorCli.Application.Stores;
using GovernorCli.Application.UseCases;
using GovernorCli.Infrastructure.Stores;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.CommandLine;

namespace GovernorCli;

internal static class Program
{
    public static int Main(string[] args)
    {
        // Build DI container (once at startup)
        var services = new ServiceCollection();
        services.AddSingleton<IBacklogStore, BacklogStore>();
        services.AddSingleton<IRunArtifactStore, RunArtifactStore>();
        services.AddSingleton<IDecisionStore, DecisionStore>();
        services.AddSingleton<IEpicStore, EpicStore>();
        services.AddSingleton<IPlanStore, PlanStore>();
        services.AddSingleton<IPatchPreviewService, PatchPreviewService>();
        services.AddSingleton<IWorkspaceStore, WorkspaceStore>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IAppDeployer, AppDeployer>();
        services.AddSingleton<RefineTechUseCase>();
        services.AddSingleton<Flows.RefineTechFlow>();
        services.AddSingleton<DeliverUseCase>();
        services.AddSingleton<IDeliverUseCase>(sp => sp.GetRequiredService<DeliverUseCase>());
        services.AddSingleton<Flows.DeliverFlow>();

        var provider = services.BuildServiceProvider();

        // ---- Global options (Recursive so they apply to subcommands) ----
        var workdirOption = new Option<string>(
            name: "--workdir",
            aliases: new[] { "-w" })
        {
            Description = "Repository root/work directory (default: current directory).",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
            Recursive = true
        };

        var verboseOption = new Option<bool>(
            name: "--verbose",
            aliases: new[] { "-v" })
        {
            Description = "Enable verbose logging.",
            Recursive = true
        };

        // ---- Root command ----
        var root = new RootCommand("Agentic SCRUM Governor CLI");
        root.Options.Add(workdirOption);
        root.Options.Add(verboseOption);

        // ---- Subcommands ----
        root.Subcommands.Add(BuildInitCommand(workdirOption, verboseOption));
        root.Subcommands.Add(BuildStatusCommand(workdirOption, verboseOption));
        root.Subcommands.Add(BuildRefineCommand(workdirOption, verboseOption));
        root.Subcommands.Add(BuildPlanCommand(workdirOption, verboseOption));
        root.Subcommands.Add(BuildReviewCommand(workdirOption, verboseOption));
        root.Subcommands.Add(BuildIntakeCommand(workdirOption, verboseOption));
        root.Subcommands.Add(BuildRefineTechCommand(provider, workdirOption, verboseOption));
        root.Subcommands.Add(BuildDeliverCommand(provider, workdirOption, verboseOption));

        // ---- Invoke ----
        return root.Parse(args).Invoke();
    }

    private static Command BuildInitCommand(Option<string> workdirOption, Option<bool> verboseOption)
    {
        var cmd = new Command("init", "Validate repository layout and required files.");

        cmd.SetAction(parseResult =>
        {
            var workdir = ResolveWorkdir(parseResult, workdirOption);
            var verbose = parseResult.GetValue(verboseOption);

            if (verbose)
            {
                AnsiConsole.MarkupLine("[grey]Verbose enabled[/]");
                AnsiConsole.MarkupLine($"[grey]Workdir:[/] {Markup.Escape(workdir)}");
            }

            // Create missing directories
            var createdDirs = RepoChecks.EnsureDirectoriesExist(workdir);
            if (createdDirs.Count > 0)
            {
                AnsiConsole.MarkupLine("[green]✓[/] Created missing directories:");
                foreach (var dir in createdDirs)
                    AnsiConsole.MarkupLine($"  - [blue]{Markup.Escape(dir)}[/]");
            }

            // Validate layout (check for missing files)
            var problems = RepoChecks.ValidateLayout(workdir);
            if (problems.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]OK[/] Repo layout valid.");
                return 0;
            }

            AnsiConsole.MarkupLine("[red]FAIL[/] Missing required files:");
            foreach (var p in problems)
                AnsiConsole.MarkupLine($"  - [red]{Markup.Escape(p)}[/]");

            return 2;
        });

        return cmd;
    }

    private static Command BuildStatusCommand(Option<string> workdirOption, Option<bool> verboseOption)
    {
        var cmd = new Command("status", "Show current state summary (placeholder for now).");

        cmd.SetAction(parseResult =>
        {
            var workdir = ResolveWorkdir(parseResult, workdirOption);
            var verbose = parseResult.GetValue(verboseOption);

            if (verbose)
                AnsiConsole.MarkupLine($"[grey]Workdir:[/] {Markup.Escape(workdir)}");

            var stateDir = Path.Combine(workdir, "state");
            AnsiConsole.MarkupLine($"State directory: [blue]{Markup.Escape(stateDir)}[/]");
            AnsiConsole.MarkupLine("Status: [yellow]Not implemented[/]");
            return 0;
        });

        return cmd;
    }

    private static Command BuildRefineCommand(Option<string> workdirOption, Option<bool> verboseOption)
    {
        var itemIdOption = new Option<int>(name: "--item")
        {
            Description = "Backlog item id to refine.",
            Required = true
        };

        var approveOption = new Option<bool>(name: "--approve")
        {
            Description = "Approve and apply the proposed patch to state/backlog.yaml."
        };

        var cmd = new Command("refine", "Run refinement flow for a backlog item (skeleton).");
        cmd.Options.Add(itemIdOption);
        cmd.Options.Add(approveOption);

        cmd.SetAction(parseResult =>
        {
            var workdir = ResolveWorkdir(parseResult, workdirOption);
            var verbose = parseResult.GetValue(verboseOption);
            var itemId = parseResult.GetValue(itemIdOption);
            var approve = parseResult.GetValue(approveOption);

            var exitCode = Flows.RefineFlow.Run(workdir, itemId, verbose, approve);

            // Minimal user-facing output
            if (exitCode == 0)
            {
                AnsiConsole.MarkupLine($"Refine: item [blue]{itemId}[/]");
                AnsiConsole.MarkupLine("[green]OK[/] Run artifacts written under state/runs/.");
            }
            else if (exitCode == 2)
            {
                AnsiConsole.MarkupLine("[red]FAIL[/] Repo layout invalid. Run `init` for details.");
            }
            else if (exitCode == 3)
            {
                AnsiConsole.MarkupLine($"[red]FAIL[/] Backlog item not found: {itemId}");
            }
            else if (exitCode == 4)
            {
                AnsiConsole.MarkupLine("[red]FAIL[/] Could not parse state/backlog.yaml");
            }
            else if (exitCode == 5)
            {
                AnsiConsole.MarkupLine("[red]FAIL[/] Definition of Ready gate failed for this item.");
                AnsiConsole.MarkupLine("See the latest run folder under [blue]state/runs/[/] for details.");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]FAIL[/] Unexpected error (exit code {exitCode}).");
            }

            return exitCode;
        });


        return cmd;
    }

    private static Command BuildPlanCommand(Option<string> workdirOption, Option<bool> verboseOption)
    {
        var cmd = new Command("plan", "Run sprint planning flow (skeleton).");

        cmd.SetAction(parseResult =>
        {
            var workdir = ResolveWorkdir(parseResult, workdirOption);
            var verbose = parseResult.GetValue(verboseOption);

            if (verbose)
                AnsiConsole.MarkupLine($"[grey]Workdir:[/] {Markup.Escape(workdir)}");

            AnsiConsole.MarkupLine("Plan: [yellow]Not implemented[/]");
            return 0;
        });

        return cmd;
    }

    private static Command BuildReviewCommand(Option<string> workdirOption, Option<bool> verboseOption)
    {
        var itemIdOption = new Option<int>(name: "--item")
        {
            Description = "Backlog item id to review.",
            Required = true
        };

        var cmd = new Command("review", "Run review flow for a backlog item (skeleton).");
        cmd.Options.Add(itemIdOption);

        cmd.SetAction(parseResult =>
        {
            var workdir = ResolveWorkdir(parseResult, workdirOption);
            var verbose = parseResult.GetValue(verboseOption);
            var itemId = parseResult.GetValue(itemIdOption);

            if (verbose)
                AnsiConsole.MarkupLine($"[grey]Workdir:[/] {Markup.Escape(workdir)}");

            AnsiConsole.MarkupLine($"Review: item [blue]{itemId}[/]");
            AnsiConsole.MarkupLine("[yellow]Not implemented[/]");
            return 0;
        });

        return cmd;
    }

    private static Command BuildIntakeCommand(Option<string> workdirOption, Option<bool> verboseOption)
    {
        var titleOption = new Option<string>("--title")
        {
            Description = "Title for the new backlog item.",
            Required = true
        };

        var storyOption = new Option<string>("--story")
        {
            Description = "Story/description for the new backlog item.",
            Required = true
        };

        var cmd = new Command("intake", "Create a new backlog item from a raw idea (deterministic).");
        cmd.Options.Add(titleOption);
        cmd.Options.Add(storyOption);

        cmd.SetAction(parseResult =>
        {
            var workdir = ResolveWorkdir(parseResult, workdirOption);
            var verbose = parseResult.GetValue(verboseOption);

            var title = parseResult.GetValue(titleOption) ?? "";
            var story = parseResult.GetValue(storyOption) ?? "";

            var exitCode = Flows.IntakeFlow.Run(workdir, title, story, verbose);

            if (exitCode == 0)
                AnsiConsole.MarkupLine("[green]OK[/] Intake completed. Backlog updated.");
            else if (exitCode == 2)
                AnsiConsole.MarkupLine("[red]FAIL[/] Repo layout invalid. Run `init` for details.");
            else if (exitCode == 4)
                AnsiConsole.MarkupLine("[red]FAIL[/] Could not parse state/backlog.yaml");
            else
                AnsiConsole.MarkupLine($"[red]FAIL[/] Unexpected error (exit code {exitCode}).");

            return exitCode;
        });

        return cmd;
    }

    private static Command BuildRefineTechCommand(IServiceProvider provider, Option<string> workdirOption, Option<bool> verboseOption)
    {
        var itemIdOption = new Option<int>(name: "--item")
        {
            Description = "Backlog item id to technically refine.",
            Required = true
        };

        var approveOption = new Option<bool>(name: "--approve")
        {
            Description = "Approve and apply the proposed tech-refinement patch to state/backlog.yaml."
        };

        var sameModelOption = new Option<bool>(name: "--same-model")
        {
            Description = "Use the same LLM model for all personas (default: uses per-persona config from .env)."
        };

        var modelSadOption = new Option<string>(name: "--model-sad")
        {
            Description = "LLM model to use for Senior Architect Developer persona."
        };

        var modelSasdOption = new Option<string>(name: "--model-sasd")
        {
            Description = "LLM model to use for Senior Audio Systems Developer persona."
        };

        var modelQaOption = new Option<string>(name: "--model-qa")
        {
            Description = "LLM model to use for QA Engineer persona."
        };

        var cmd = new Command("refine-tech", "Run technical refinement & readiness flow for a backlog item.");
        cmd.Options.Add(itemIdOption);
        cmd.Options.Add(approveOption);
        cmd.Options.Add(sameModelOption);
        cmd.Options.Add(modelSadOption);
        cmd.Options.Add(modelSasdOption);
        cmd.Options.Add(modelQaOption);

        cmd.SetAction(parseResult =>
        {
            var workdir = ResolveWorkdir(parseResult, workdirOption);
            var verbose = parseResult.GetValue(verboseOption);
            var itemId = parseResult.GetValue(itemIdOption);
            var approve = parseResult.GetValue(approveOption);
            var sameModel = parseResult.GetValue(sameModelOption);
            var modelSad = parseResult.GetValue(modelSadOption);
            var modelSasd = parseResult.GetValue(modelSasdOption);
            var modelQa = parseResult.GetValue(modelQaOption);

            // Resolve flow from DI container
            var flow = provider.GetRequiredService<Flows.RefineTechFlow>();
            var exitCode = flow.Execute(workdir, itemId, verbose, approve, sameModel, modelSad, modelSasd, modelQa);

            if (exitCode == Domain.Enums.FlowExitCode.Success)
            {
                AnsiConsole.MarkupLine($"Refine-Tech: item [blue]{itemId}[/]");
                AnsiConsole.MarkupLine(approve
                    ? "[green]✓[/] Approved. Backlog updated and decision logged."
                    : "[green]✓[/] Preview written (no backlog changes).");
                AnsiConsole.MarkupLine("[grey]See state/runs/ for artifacts.[/]");
            }
            else if (exitCode == Domain.Enums.FlowExitCode.InvalidRepoLayout)
                AnsiConsole.MarkupLine("[red]✗ FAIL[/] Repo layout invalid. Run `init` for details.");
            else if (exitCode == Domain.Enums.FlowExitCode.ItemNotFound)
                AnsiConsole.MarkupLine($"[red]✗ FAIL[/] Backlog item not found: {itemId}");
            else if (exitCode == Domain.Enums.FlowExitCode.BacklogParseError)
                AnsiConsole.MarkupLine("[red]✗ FAIL[/] Could not parse state/backlog.yaml");
            else if (exitCode == Domain.Enums.FlowExitCode.ApplyFailed)
                AnsiConsole.MarkupLine("[red]✗ FAIL[/] Failed to apply patch.");
            else
                AnsiConsole.MarkupLine($"[red]✗ FAIL[/] Unexpected error (exit code {(int)exitCode}).");

            // ✅ Return int (not Environment.Exit) - lets System.CommandLine handle propagation
            return (int)exitCode;
        });

        return cmd;

    }

    private static Command BuildDeliverCommand(IServiceProvider provider, Option<string> workdirOption, Option<bool> verboseOption)
    {
        var itemIdOption = new Option<int>(name: "--item")
        {
            Description = "Backlog item id to deliver.",
            Required = true
        };

        var approveOption = new Option<bool>(name: "--approve")
        {
            Description = "Approve and deploy the candidate implementation to /apps/<appId>/."
        };

        var cmd = new Command("deliver", "Generate, validate, and deploy a backlog item.");
        cmd.Options.Add(itemIdOption);
        cmd.Options.Add(approveOption);

        cmd.SetAction(parseResult =>
        {
            var workdir = ResolveWorkdir(parseResult, workdirOption);
            var verbose = parseResult.GetValue(verboseOption);
            var itemId = parseResult.GetValue(itemIdOption);
            var approve = parseResult.GetValue(approveOption);

            // Resolve flow from DI container
            var flow = provider.GetRequiredService<Flows.DeliverFlow>();
            var exitCode = flow.Execute(workdir, itemId, verbose, approve);

            if (exitCode == Domain.Enums.FlowExitCode.Success)
            {
                AnsiConsole.MarkupLine($"Deliver: item [blue]{itemId}[/]");
                AnsiConsole.MarkupLine(approve
                    ? "[green]✓[/] Approved and deployed. Workspace candidate copied to /apps/. Decision logged."
                    : "[green]✓[/] Validation passed. Preview written. Run with --approve to deploy.");
                AnsiConsole.MarkupLine("[grey]See state/runs/ for artifacts.[/]");
            }
            else if (exitCode == Domain.Enums.FlowExitCode.InvalidRepoLayout)
                AnsiConsole.MarkupLine("[red]✗ FAIL[/] Repo layout invalid. Run `init` for details.");
            else if (exitCode == Domain.Enums.FlowExitCode.ItemNotFound)
                AnsiConsole.MarkupLine($"[red]✗ FAIL[/] Backlog item not found: {itemId}");
            else if (exitCode == Domain.Enums.FlowExitCode.BacklogParseError)
                AnsiConsole.MarkupLine("[red]✗ FAIL[/] Could not parse state/backlog.yaml");
            else if (exitCode == Domain.Enums.FlowExitCode.PreconditionFailed)
                AnsiConsole.MarkupLine("[red]✗ FAIL[/] Item does not meet preconditions. Run 'governor refine-tech --item X --approve' first to generate the implementation plan.");
            else if (exitCode == Domain.Enums.FlowExitCode.ValidationFailed)
                AnsiConsole.MarkupLine("[red]✗ FAIL[/] Validation failed. See state/runs/ for details. Fix and retry.");
            else if (exitCode == Domain.Enums.FlowExitCode.ApplyFailed)
                AnsiConsole.MarkupLine("[red]✗ FAIL[/] Failed to deploy implementation.");
            else if (exitCode == Domain.Enums.FlowExitCode.UnexpectedError)
                AnsiConsole.MarkupLine($"[red]✗ FAIL[/] Unexpected error. Check logs for details.");
            else
                AnsiConsole.MarkupLine($"[red]✗ FAIL[/] Unexpected error (exit code {(int)exitCode}).");

            return (int)exitCode;
        });

        return cmd;
    }

    private static string ResolveWorkdir(ParseResult parseResult, Option<string> workdirOption)
    {
        var workdir = parseResult.GetValue(workdirOption) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(workdir);
    }
}

public static class RepoChecks
{
    /// <summary>
    /// Create all required directories if they don't exist.
    /// Returns a list of directories that were created.
    /// </summary>
    public static List<string> EnsureDirectoriesExist(string workdir)
    {
        var created = new List<string>();

        // Required directories
        var requiredDirs = new[]
        {
            "src",
            "state",
            "prompts",
            Path.Combine("prompts", "personas"),
            Path.Combine("prompts", "flows"),
            Path.Combine("state", "decisions"),
            Path.Combine("state", "runs"),
            Path.Combine("state", "plans"),
            "apps"
        };

        foreach (var rel in requiredDirs)
        {
            var fullPath = Path.Combine(workdir, rel);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
                created.Add(rel);
            }
        }

        return created;
    }

    public static List<string> ValidateLayout(string workdir)
    {
        var problems = new List<string>();

        // Required dirs
        RequireDir(workdir, "src", problems);
        RequireDir(workdir, "state", problems);
        RequireDir(workdir, "prompts", problems);
        RequireDir(workdir, Path.Combine("prompts", "personas"), problems);
        RequireDir(workdir, Path.Combine("prompts", "flows"), problems);
        RequireDir(workdir, Path.Combine("state", "decisions"), problems);
        RequireDir(workdir, Path.Combine("state", "runs"), problems);
        RequireDir(workdir, Path.Combine("state", "plans"), problems);
        RequireDir(workdir, "apps", problems);

        // Required state files
        RequireFile(workdir, Path.Combine("state", "team-board.md"), problems);
        RequireFile(workdir, Path.Combine("state", "backlog.yaml"), problems);
        RequireFile(workdir, Path.Combine("state", "risks.md"), problems);
        RequireFile(workdir, Path.Combine("state", "decisions", "decision-log.md"), problems);
        RequireFile(workdir, Path.Combine("state", "epics.yaml"), problems);


        // Persona prompts
        RequireFile(workdir, Path.Combine("prompts", "personas", "product-owner.md"), problems);
        RequireFile(workdir, Path.Combine("prompts", "personas", "senior-architect-dev.md"), problems);
        RequireFile(workdir, Path.Combine("prompts", "personas", "senior-audio-dev.md"), problems);
        RequireFile(workdir, Path.Combine("prompts", "personas", "qa-engineer.md"), problems);
        RequireFile(workdir, Path.Combine("prompts", "personas", "music-biz-specialist.md"), problems);

        // Flow prompts
        RequireFile(workdir, Path.Combine("prompts", "flows", "intake.md"), problems);
        RequireFile(workdir, Path.Combine("prompts", "flows", "refine.md"), problems);
        RequireFile(workdir, Path.Combine("prompts", "flows", "sprint-planning.md"), problems);
        RequireFile(workdir, Path.Combine("prompts", "flows", "review.md"), problems);

        return problems;
    }

    private static void RequireDir(string workdir, string rel, List<string> problems)
    {
        var p = Path.Combine(workdir, rel);
        if (!Directory.Exists(p)) problems.Add($"Missing directory: {rel}");
    }

    private static void RequireFile(string workdir, string rel, List<string> problems)
    {
        var p = Path.Combine(workdir, rel);
        if (!File.Exists(p)) problems.Add($"Missing file: {rel}");
    }
}
