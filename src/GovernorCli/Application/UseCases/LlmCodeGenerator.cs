using System.Text.Json;
using GovernorCli.Application.Models;
using GovernorCli.LanguageModel;
using GovernorCli.Personas;
using GovernorCli.State;

namespace GovernorCli.Application.UseCases;

public class LlmCodeGenerator
{
    private static readonly string[] GenerationPhases = { "core", "tests", "config" };

    public async Task<CodeGenerationSpec> GenerateCodeAsync(
        ImplementationPlan plan,
        BacklogItem item,
        string workdir,
        string workspaceRoot,
        string runDir,
        PersonaModelConfig modelConfig,
        string phase,
        string? architectureContent = null,
        string? qaPlanContent = null,
        string? tasksContent = null)
    {
        var phaseFileName = $"code-{phase}.json";
        var phaseFilePath = Path.Combine(runDir, phaseFileName);

        if (File.Exists(phaseFilePath))
        {
            var existingJson = File.ReadAllText(phaseFilePath);
            var existingSpec = JsonSerializer.Deserialize<CodeGenerationSpec>(existingJson);
            if (existingSpec != null)
            {
                WriteFilesToWorkspace(existingSpec, workspaceRoot);
                return existingSpec;
            }
        }

        var spec = await GenerateForPhase(
            plan, item, workdir, workspaceRoot, modelConfig, phase,
            architectureContent, qaPlanContent, tasksContent);

        spec.Phase = phase;
        spec.GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("O");

        var json = JsonSerializer.Serialize(spec, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(phaseFilePath, json);

        WriteFilesToWorkspace(spec, workspaceRoot);

        return spec;
    }

    public async Task<CodeGenerationSpec> GenerateAllPhasesAsync(
        ImplementationPlan plan,
        BacklogItem item,
        string workdir,
        string workspaceRoot,
        string runDir,
        PersonaModelConfig modelConfig,
        string? architectureContent = null,
        string? qaPlanContent = null,
        string? tasksContent = null)
    {
        var allSpec = new CodeGenerationSpec();

        foreach (var phase in GenerationPhases)
        {
            var phaseSpec = await GenerateCodeAsync(
                plan, item, workdir, workspaceRoot, runDir, modelConfig, phase,
                architectureContent, qaPlanContent, tasksContent);

            allSpec.SourceFiles.AddRange(phaseSpec.SourceFiles);
            allSpec.TestFiles.AddRange(phaseSpec.TestFiles);
            allSpec.ConfigFiles.AddRange(phaseSpec.ConfigFiles);
        }

        return allSpec;
    }

    private async Task<CodeGenerationSpec> GenerateForPhase(
        ImplementationPlan plan,
        BacklogItem item,
        string workdir,
        string workspaceRoot,
        PersonaModelConfig modelConfig,
        string phase,
        string? architectureContent,
        string? qaPlanContent,
        string? tasksContent)
    {
        var flowPrompt = LoadFlowPrompt(workdir, "code-generation.md");
        
        var personaId = phase switch
        {
            "core" => PersonaId.SAD,
            "tests" => PersonaId.QA,
            "config" => PersonaId.SASD,
            _ => PersonaId.SAD
        };

        var personaPrompt = LoadPersonaPrompt(workdir, personaId);
        var provider = PersonaLlmProviderFactory.Create(personaId, modelConfig);

        var context = BuildCodeGenerationContext(plan, item, phase, architectureContent, qaPlanContent, tasksContent, workspaceRoot);

        var contractInstruction = GetPhaseContractInstruction(phase);

        var lmRequest = new LanguageModelRequest(
            provider.PersonaId,
            personaPrompt,
            flowPrompt + "\n\n" + contractInstruction,
            context);

        var response = await provider.GenerateAsync(lmRequest, CancellationToken.None);

        return ParseCodeGenerationResponse(response.OutputText, phase);
    }

    private string BuildCodeGenerationContext(
        ImplementationPlan plan,
        BacklogItem item,
        string phase,
        string? architectureContent,
        string? qaPlanContent,
        string? tasksContent,
        string workspaceRoot)
    {
        var isUpdate = !string.IsNullOrEmpty(plan.EpicId) || item.Id > 0;
        var existingCode = "";

        if (isUpdate && Directory.Exists(workspaceRoot))
        {
            existingCode = LoadExistingCode(workspaceRoot);
        }

        var context = $"""
            BACKLOG ITEM:
            - ID: {item.Id}
            - Title: {item.Title}
            - Story: {item.Story}
            
            Acceptance Criteria:
            {(item.AcceptanceCriteria.Count == 0 ? "(none)" : string.Join("\n", item.AcceptanceCriteria))}
            
            IMPLEMENTATION PLAN:
            - App Type: {plan.AppType}
            - Stack: {plan.Stack.Language} {plan.Stack.Runtime} {plan.Stack.Framework}
            - Target: {plan.RepoTarget}
            
            GENERATION PHASE: {phase}
            
            """;

        if (!string.IsNullOrEmpty(architectureContent))
        {
            context += $"""
                ARCHITECTURE:
                {architectureContent}
                
                """;
        }

        if (!string.IsNullOrEmpty(qaPlanContent) && phase == "tests")
        {
            context += $"""
                QA PLAN:
                {qaPlanContent}
                
                """;
        }

        if (!string.IsNullOrEmpty(tasksContent))
        {
            context += $"""
                TECHNICAL TASKS:
                {tasksContent}
                
                """;
        }

        if (!string.IsNullOrEmpty(existingCode))
        {
            context += $"""
                EXISTING CODE (for updates):
                {existingCode}
                
                IMPORTANT: This is an UPDATE to existing code. Generate only new/changed files.
                """;
        }
        else
        {
            context += """
                IMPORTANT: This is a NEW application. Generate complete source files.
                """;
        }

        return context;
    }

    private string GetPhaseContractInstruction(string phase)
    {
        return phase switch
        {
            "core" => """
                PHASE: CORE
                Generate main application source code (Program.cs, services, models, etc.)
                Output as JSON with "files" array.
                """,
            "tests" => """
                PHASE: TESTS
                Generate unit tests and integration tests.
                Output as JSON with "files" array.
                """,
            "config" => """
                PHASE: CONFIG
                Generate project files (.csproj), config files, .gitignore, README.
                Output as JSON with "files" array.
                """,
            _ => "Generate the code files as JSON."
        };
    }

    private CodeGenerationSpec ParseCodeGenerationResponse(string responseText, string phase)
    {
        var spec = new CodeGenerationSpec { Phase = phase };

        try
        {
            var cleaned = responseText.Trim();
            if (cleaned.StartsWith("```json"))
                cleaned = cleaned.Substring("```json".Length);
            if (cleaned.StartsWith("```"))
                cleaned = cleaned.Substring("```".Length);
            if (cleaned.EndsWith("```"))
                cleaned = cleaned.Substring(0, cleaned.Length - 3);

            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            if (root.TryGetProperty("files", out var files))
            {
                foreach (var file in files.EnumerateArray())
                {
                    var path = file.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                    var content = file.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                    var language = file.TryGetProperty("language", out var l) ? l.GetString() ?? "" : "";

                    spec.SourceFiles.Add(new SourceFileSpec
                    {
                        Path = path,
                        Content = content,
                        Language = language,
                        Kind = phase
                    });
                }
            }
        }
        catch (Exception ex)
        {
            spec.SourceFiles.Add(new SourceFileSpec
            {
                Path = $"error-{phase}.txt",
                Content = $"Failed to parse LLM response: {ex.Message}\n\nOriginal:\n{responseText}",
                Language = "text"
            });
        }

        return spec;
    }

    private void WriteFilesToWorkspace(CodeGenerationSpec spec, string workspaceRoot)
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

    private string LoadExistingCode(string workspaceRoot)
    {
        var files = Directory.GetFiles(workspaceRoot, "*.cs", SearchOption.AllDirectories);
        var code = new List<string>();

        foreach (var file in files.Take(10))
        {
            var relativePath = Path.GetRelativePath(workspaceRoot, file);
            code.Add($"\n--- {relativePath} ---\n");
            code.Add(File.ReadAllText(file));
        }

        return string.Join("\n", code);
    }

    private string LoadFlowPrompt(string workdir, string promptFileName)
    {
        var path = Path.Combine(workdir, "prompts", "flows", promptFileName);
        if (!File.Exists(path))
            return "";
        return File.ReadAllText(path);
    }

    private string LoadPersonaPrompt(string workdir, PersonaId personaId)
    {
        var promptFileName = personaId switch
        {
            PersonaId.SAD => "senior-architect-dev.md",
            PersonaId.SASD => "senior-audio-dev.md",
            PersonaId.QA => "qa-engineer.md",
            _ => "senior-architect-dev.md"
        };

        var path = Path.Combine(workdir, "prompts", "personas", promptFileName);
        if (!File.Exists(path))
            return "";
        return File.ReadAllText(path);
    }
}
