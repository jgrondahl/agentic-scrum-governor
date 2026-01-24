using System.Security.Cryptography;
using System.Text;

namespace GovernorCli.LanguageModel;

public sealed class StubLanguageModelProvider : ILanguageModelProvider
{
    public string Name => "stub";

    public Task<LanguageModelResponse> GenerateAsync(LanguageModelRequest request, CancellationToken ct)
    {
        // Deterministic output based on inputs (so runs are reproducible).
        var hash = ComputeShortHash($"{request.PersonaId}|{request.FlowPrompt}|{request.InputContext}");

        var output =
        $"""
        # {request.PersonaId} Output (Stub)

        This is a deterministic stub response.
        Hash: {hash}

        ## Notes
        - Replace this provider with OpenAI later.
        - Output contract enforcement comes next step.
        """;

        return Task.FromResult(new LanguageModelResponse(
            PersonaId: request.PersonaId,
            OutputText: output,
            Metadata: new Dictionary<string, string>
            {
                ["provider"] = Name,
                ["hash"] = hash
            }));
    }

    private static string ComputeShortHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).Substring(0, 12);
    }
}
