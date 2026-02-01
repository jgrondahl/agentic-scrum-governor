using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GovernorCli.LanguageModel;

public sealed class OpenAiLanguageModelProvider : ILanguageModelProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly Dictionary<string, JsonElement> _schemaCache = new();

    public string Name => "openai";

    public OpenAiLanguageModelProvider(HttpClient http, string apiKey, string model)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? throw new ArgumentException("API key is required.", nameof(apiKey)) : apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? throw new ArgumentException("Model is required.", nameof(model)) : model;
    }

    public async Task<LanguageModelResponse> GenerateAsync(LanguageModelRequest request, CancellationToken ct)
    {
        var schemaElement = GetOrLoadSchema(request.PersonaId);

        var body = new
        {
            model = _model,
            store = false,

            // Keep output bounded while iterating; override via env var if needed
            max_output_tokens = GetMaxOutputTokens(),

            // Structured Outputs for Responses API:
            // text: { format: { type: "json_schema", strict: true, schema: ... } }
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = $"{request.PersonaId.ToLowerInvariant()}_refine",
                    schema = schemaElement,
                    strict = true
                }
            },

            input = new object[]
            {
                new { role = "system", content = request.PersonaPrompt },
                new { role = "developer", content = request.FlowPrompt },
                new { role = "user", content = request.InputContext }
            }
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        msg.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(msg, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            // Return the error payload; your run artifacts will capture it.
            return new LanguageModelResponse(
                PersonaId: request.PersonaId,
                OutputText: json,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = Name,
                    ["http_status"] = ((int)resp.StatusCode).ToString(),
                    ["model"] = _model
                });
        }

        // With structured outputs, the model should return valid JSON.
        // Try to extract output text; if not found, return full JSON response.
        var outputText = TryExtractOutputText(json) ?? json;

        return new LanguageModelResponse(
            PersonaId: request.PersonaId,
            OutputText: outputText,
            Metadata: new Dictionary<string, string>
            {
                ["provider"] = Name,
                ["model"] = _model
            });
    }

    private static int GetMaxOutputTokens()
    {
        var raw = Environment.GetEnvironmentVariable("OPENAI_MAX_OUTPUT_TOKENS");
        return int.TryParse(raw, out var v) && v > 0 ? v : 900;
    }

    private JsonElement GetOrLoadSchema(string personaId)
    {
        var key = personaId.ToLowerInvariant();
        if (_schemaCache.TryGetValue(key, out var cached))
            return cached;

        var schema = LoadSchemaAsJsonElement(personaId);
        _schemaCache[key] = schema;
        return schema;
    }

    // We load schemas from repo root so the contract is a first-class artifact.
    // Set GOVENOR_WORKDIR=/work in docker-compose; locally run from repo root or set it explicitly.
    private static string ResolveWorkdir()
    {
        var wd = Environment.GetEnvironmentVariable("GOVERNOR_WORKDIR");
        if (!string.IsNullOrWhiteSpace(wd)) return wd;
        return Directory.GetCurrentDirectory();
    }

    private static JsonElement LoadSchemaAsJsonElement(string personaId)
    {
        var workdir = ResolveWorkdir();
        var schemaPath = Path.Combine(workdir, "schemas", "refine", $"{personaId.ToLowerInvariant()}.schema.json");

        if (!File.Exists(schemaPath))
            throw new FileNotFoundException($"Schema file missing: {schemaPath}", schemaPath);

        var schemaText = File.ReadAllText(schemaPath);

        try
        {
            using var doc = JsonDocument.Parse(schemaText);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Schema file contains invalid JSON: {schemaPath}. " +
                $"Ensure the file is valid JSON format.",
                ex);
        }
    }

    private static string? TryExtractOutputText(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // 1) Structured Outputs: prefer output_parsed if present.
            // This is the schema-conformant object we actually want to validate downstream.
            if (root.TryGetProperty("output_parsed", out var op) &&
                (op.ValueKind == JsonValueKind.Object || op.ValueKind == JsonValueKind.Array))
            {
                return JsonSerializer.Serialize(op, new JsonSerializerOptions { WriteIndented = false });
            }

            // 2) Primary message extraction: traverse output[] -> message -> content[] -> output_text.text
            if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();

                foreach (var item in output.EnumerateArray())
                {
                    if (!item.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                        continue;

                    if (!string.Equals(typeEl.GetString(), "message", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!item.TryGetProperty("content", out var contentEl) || contentEl.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var c in contentEl.EnumerateArray())
                    {
                        if (c.TryGetProperty("type", out var ct) && ct.ValueKind == JsonValueKind.String &&
                            string.Equals(ct.GetString(), "output_text", StringComparison.OrdinalIgnoreCase) &&
                            c.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                        {
                            sb.Append(textEl.GetString());
                        }
                    }
                }

                var s = sb.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }

            // 3) Convenience field sometimes present
            if (root.TryGetProperty("output_text", out var ot) && ot.ValueKind == JsonValueKind.String)
            {
                var s = ot.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }

            // 4) No usable extracted payload
            return null;
        }
        catch
        {
            return null;
        }
    }
}
