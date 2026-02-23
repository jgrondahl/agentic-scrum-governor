using GovernorCli.State;

namespace GovernorCli.Application.Models;

/// <summary>
/// Preview of proposed changes (read-only, not yet applied).
/// Written to: patch.preview.json
/// Used by: CLI to show user what would change
/// Never overwrites: patch.json (approved state)
/// </summary>
public sealed record PatchPreview
{
    public required string ComputedAtUtc { get; init; }
    public required int ItemId { get; init; }
    public required PatchChanges Changes { get; init; }
}

/// <summary>
/// Snapshot of what will change (before → after for each field).
/// </summary>
public sealed record PatchChanges
{
    public required StatusChange Status { get; init; }
    public required EstimateChange Estimate { get; init; }
    public required RefChange TechnicalNotesRef { get; init; }
}

public sealed record StatusChange
{
    public required string Before { get; init; }
    public required string After { get; init; }
}

public sealed record EstimateChange
{
    public required BacklogEstimate? Before { get; init; }
    public required BacklogEstimate? After { get; init; }
}

public sealed record RefChange
{
    public required string? Before { get; init; }
    public required string After { get; init; }
}

/// <summary>
/// Applied patch record (final authoritative state).
/// Written to: patch.json (after approval)
/// Used for: Audit trail, decision history
/// </summary>
public sealed record AppliedPatch
{
    public required string AppliedAtUtc { get; init; }
    public required int ItemId { get; init; }
    public required string RunId { get; init; }
    public required PatchChanges Changes { get; init; }
}
