using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GovernorCli.LanguageModel;

public sealed class StubLanguageModelProvider : ILanguageModelProvider
{
    public string Name => "stub";

    public Task<LanguageModelResponse> GenerateAsync(LanguageModelRequest request, CancellationToken ct)
    {
        var hash = ComputeShortHash($"{request.PersonaId}|{request.FlowPrompt}|{request.InputContext}");

        var payload = BuildPersonaPayload(request.PersonaId, hash);

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(new LanguageModelResponse(
            PersonaId: request.PersonaId,
            OutputText: json,
            Metadata: new Dictionary<string, string>
            {
                ["provider"] = Name,
                ["hash"] = hash
            }));
    }

    private static object BuildPersonaPayload(string personaId, string hash) =>
        personaId switch
        {
            "PO" => new
            {
                risks = new[] { "Scope creep risk (stub)." },
                assumptions = new[] { "User wants a minimal MVP (stub)." },
                recommendations = new[] { "Clarify acceptance criteria into measurable bullets (stub)." },
                acceptanceCriteriaUpdates = new[] { "Add at least one measurable output condition (stub)." },
                nonGoalsUpdates = new[] { "Explicitly exclude integrations in MVP (stub)." },
                prioritySuggestion = 1,
                _stub = new { hash }
            },

            "MIBS" => new
            {
                risks = new[] { "Weak differentiation risk (stub)." },
                assumptions = new[] { "Target user is budget-sensitive (stub)." },
                recommendations = new[] { "Position as a utility with fast feedback (stub)." },
                icp = "Electronic music producers working in-the-box (stub).",
                positioning = "Fast, objective mix feedback without uploading audio (stub).",
                pricingHypothesis = "$19 one-time or $5/mo (stub).",
                scopeTraps = new[] { "Avoid AI mastering claims in MVP (stub)." },
                _stub = new { hash }
            },

            "SAD" => new
            {
                storyPoints = 3,
                confidence = "medium",
                complexityDrivers = new[] { "Clean Architecture patterns", "Multi-agent coordination" },
                assumptions = new[] { "CLI drives workflow; state stored in repo" },
                dependencies = new[] { "LLM API access" },
                rationale = "Standard implementation with clear separation of concerns.",
                appType = "web_blazor",
                language = "csharp",
                runtime = "net8.0",
                framework = "blazor",
                projects = new[]
                {
                    new { name = "JeremyRavine.Web", type = "web", path = "src/JeremyRavine.Web/JeremyRavine.Web.csproj", dependencies = new[] { "JeremyRavine.Core" } },
                    new { name = "JeremyRavine.Core", type = "library", path = "src/JeremyRavine.Core/JeremyRavine.Core.csproj", dependencies = new string[] { } },
                    new { name = "JeremyRavine.Tests", type = "test", path = "tests/JeremyRavine.Tests/JeremyRavine.Tests.csproj", dependencies = new[] { "JeremyRavine.Core" } }
                },
                risks = new[] { "Boundary drift risk if outputs remain unstructured (stub)." },
                recommendations = new[] { "Keep flows pure and push IO to adapters (stub)." },
                architectureChanges = new[] { "Add contract validation + retry gate (stub)." },
                interfaceNotes = new[] { "LanguageModel provider remains a port; vendor adapters later (stub)." },
                nfrs = new[] { "Deterministic exit codes", "Auditable run artifacts" },
                _stub = new { hash }
            },

            "SASD" => new
            {
                risks = new[] { "Audio quality claims without objective metrics (stub)." },
                assumptions = new[] { "MVP uses objective analysis rather than subjective grading (stub)." },
                recommendations = new[] { "Define metrics early: LUFS, crest factor, spectral tilt (stub)." },
                dspApproach = "Start with offline analysis (FFT-based features) (stub).",
                metrics = new[] { "Integrated LUFS", "Short-term LUFS", "Crest factor", "Spectral centroid" },
                constraints = new[] { "No destructive processing in MVP", "Deterministic outputs" },
                _stub = new { hash }
            },

            "QA" => new
            {
                risks = new[] { "Non-testable acceptance criteria (stub)." },
                assumptions = new[] { "Golden fixtures exist for analysis validation (stub)." },
                recommendations = new[] { "Use golden audio fixtures and snapshot numeric outputs (stub)." },
                testOracles = new[] { "Given fixture A, metric X equals expected within tolerance (stub)." },
                edgeCases = new[] { "Silent audio", "Clipped audio", "Very short clips" },
                dodChecklist = new[] { "Unit tests exist", "Integration test runs in Docker", "Artifacts written to state/runs" },
                _stub = new { hash }
            },

            _ => new
            {
                risks = new[] { "Unknown persona (stub)." },
                assumptions = new[] { "PersonaId not recognized (stub)." },
                recommendations = new[] { "Fix persona catalog (stub)." },
                _stub = new { hash }
            }
        };

    private static string ComputeShortHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..12];
    }
}
