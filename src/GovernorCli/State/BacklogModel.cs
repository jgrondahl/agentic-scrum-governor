using System.Text.Json.Serialization;
using GovernorCli.Domain.Enums;

namespace GovernorCli.State;

public sealed class BacklogFile
{
    [JsonPropertyName("backlog")]
    public List<BacklogItem> Backlog { get; set; } = new();
}

public sealed class BacklogItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "candidate";

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    [JsonPropertyName("size")]
    public string Size { get; set; } = "S";

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = "PO";

    [JsonPropertyName("estimate")]
    public BacklogEstimate? Estimate { get; set; }

    [JsonPropertyName("technical_notes_ref")]
    public string? TechnicalNotesRef { get; set; }

    [JsonPropertyName("story")]
    public string Story { get; set; } = "";

    [JsonPropertyName("acceptance_criteria")]
    public List<string> AcceptanceCriteria { get; set; } = new();

    [JsonPropertyName("non_goals")]
    public List<string> NonGoals { get; set; } = new();

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();

    [JsonPropertyName("risks")]
    public List<string> Risks { get; set; } = new();

    [JsonPropertyName("epic_id")]
    public string? EpicId { get; set; }

    [JsonPropertyName("implementation_plan_ref")]
    public string? ImplementationPlanRef { get; set; }
}

public sealed class BacklogEstimate
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("story_points")]
    public int StoryPoints { get; set; }

    [JsonPropertyName("scale")]
    public string Scale { get; set; } = "fibonacci";

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "medium";

    [JsonPropertyName("risk_level")]
    public string RiskLevel { get; set; } = "medium";

    [JsonPropertyName("complexity_drivers")]
    public List<string> ComplexityDrivers { get; set; } = new();

    [JsonPropertyName("assumptions")]
    public List<string> Assumptions { get; set; } = new();

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();

    [JsonPropertyName("non_goals")]
    public List<string> NonGoals { get; set; } = new();

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    [JsonPropertyName("created_at_utc")]
    public string CreatedAtUtc { get; set; } = "";

    [JsonPropertyName("created_from_run_id")]
    public string CreatedFromRunId { get; set; } = "";
}