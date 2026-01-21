using System.CommandLine;
using System.CommandLine.Parsing;
using Spectre.Console;

namespace GovernorCli;

internal static class Program
{
    public static int Main(string[] args)
    {
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

            var problems = RepoChecks.ValidateLayout(workdir);
            if (problems.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]OK[/] Repo layout valid.");
                return 0;
            }

            AnsiConsole.MarkupLine("[red]FAIL[/] Repo layout invalid:");
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

        var cmd = new Command("refine", "Run refinement flow for a backlog item (skeleton).");
        cmd.Options.Add(itemIdOption);

        cmd.SetAction(parseResult =>
        {
            var workdir = ResolveWorkdir(parseResult, workdirOption);
            var verbose = parseResult.GetValue(verboseOption);
            var itemId = parseResult.GetValue(itemIdOption);

            var exitCode = Flows.RefineFlow.Run(workdir, itemId, verbose);

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

    private static string ResolveWorkdir(ParseResult parseResult, Option<string> workdirOption)
    {
        var workdir = parseResult.GetValue(workdirOption) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(workdir);
    }
}

internal static class RepoChecks
{
    public static List<string> ValidateLayout(string workdir)
    {
        var problems = new List<string>();

        // Required dirs
        RequireDir(workdir, "src", problems);
        RequireDir(workdir, "state", problems);
        RequireDir(workdir, "prompts", problems);
        RequireDir(workdir, Path.Combine("prompts", "personas"), problems);
        RequireDir(workdir, Path.Combine("prompts", "flows"), problems);

        // Required state files
        RequireFile(workdir, Path.Combine("state", "team-board.md"), problems);
        RequireFile(workdir, Path.Combine("state", "backlog.yaml"), problems);
        RequireFile(workdir, Path.Combine("state", "risks.md"), problems);

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
