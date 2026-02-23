using GovernorCli.Application.Models.Deliver;

namespace GovernorCli.Application.UseCases;

/// <summary>
/// Request for delivery operation.
/// All decision context comes from Flow (approver, runId, paths).
/// TemplateId comes from Phase 2 output (backlog item field).
/// UseCase does not read environment or format decisions.
/// </summary>
public class DeliverRequest
{
    public int ItemId { get; set; }
    public string AppId { get; set; } = "";
    public string TemplateId { get; set; } = "";  // From Phase 2 / backlog item
    public string BacklogPath { get; set; } = "";
    public string RunsDir { get; set; } = "";
    public string RunDir { get; set; } = "";       // Created by Flow
    public string Workdir { get; set; } = "";
    public string WorkspaceRoot { get; set; } = "";
    public string RunId { get; set; } = "";
    public bool Approve { get; set; }
}

/// <summary>
/// Result of delivery operation.
/// Contains all artifacts and outcome.
/// </summary>
public class DeliverResponse
{
    public bool Success { get; set; }
    public bool ValidationPassed { get; set; }
    public DeliverResult Result { get; set; } = new();
}
