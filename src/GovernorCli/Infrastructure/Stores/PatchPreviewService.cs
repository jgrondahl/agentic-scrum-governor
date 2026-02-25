using System.Security.Cryptography;
using System.Text.Json;
using GovernorCli.Application.Models;
using GovernorCli.Application.Stores;

namespace GovernorCli.Infrastructure.Stores;

/// <summary>
/// Service to compute and format patch previews.
/// Compares candidate implementation plan against approved plan (if exists).
/// Produces typed preview and human-readable diff lines.
/// </summary>
public class PatchPreviewService : IPatchPreviewService
{
    public PatchPreviewData ComputePatchPreview(string workdir, int itemId, string candidatePlanPath)
    {
        if (!File.Exists(candidatePlanPath))
            throw new FileNotFoundException($"Candidate plan not found: {candidatePlanPath}");

        // Load candidate plan
        var candidateJson = File.ReadAllText(candidatePlanPath);
        var candidatePlan = JsonSerializer.Deserialize<ImplementationPlan>(candidateJson)
            ?? throw new InvalidOperationException("Failed to deserialize candidate plan");

        // Compute plan directory path
        var approvedPlanPath = Path.Combine(workdir, "state", "plans", $"item-{itemId}", "implementation.plan.json");
        var approvedPlan = LoadApprovedPlanIfExists(approvedPlanPath);

        // For MVP, we compare the plan itself as a simple change indicator
        // In a real system, you'd diff the project_layout, build_plan, etc. to identify file changes
        var changes = ComputePlanChanges(approvedPlan, candidatePlan, approvedPlanPath);

        return new PatchPreviewData
        {
            ComputedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            ItemId = itemId,
            Changes = changes
        };
    }

    public List<string> FormatDiffLines(PatchPreviewData preview)
    {
        var lines = new List<string>();

        foreach (var change in preview.Changes)
        {
            // Normalize path to use forward slashes (repo-relative)
            var normalizedPath = change.Path.Replace("\\", "/");
            lines.Add($"{change.Action} {normalizedPath}");
        }

        return lines;
    }

    private ImplementationPlan? LoadApprovedPlanIfExists(string approvedPlanPath)
    {
        if (!File.Exists(approvedPlanPath))
            return null;

        try
        {
            var json = File.ReadAllText(approvedPlanPath);
            return JsonSerializer.Deserialize<ImplementationPlan>(json);
        }
        catch
        {
            return null;
        }
    }

    private List<PatchFileChange> ComputePlanChanges(
        ImplementationPlan? approved,
        ImplementationPlan candidate,
        string approvedPlanPath)
    {
        var changes = new List<PatchFileChange>();

        if (approved == null)
        {
            // New plan: all project files are "added"
            foreach (var file in candidate.ProjectLayout)
            {
                var candidatePath = Path.Combine(candidate.RepoTarget, file.Path);
                changes.Add(new PatchFileChange
                {
                    Action = "A",
                    Path = candidatePath,
                    CandidateSizeBytes = 0, // Placeholder in MVP
                    CandidateSha256 = "",
                    RepoSizeBytes = 0,
                    RepoSha256 = ""
                });
            }
        }
        else
        {
            // Existing plan: check for modifications
            // MVP: simple comparison of plan IDs; in full system, diff project layouts
            if (candidate.PlanId != approved.PlanId || candidate.Notes != approved.Notes)
            {
                // Mark plan file itself as modified
                changes.Add(new PatchFileChange
                {
                    Action = "M",
                    Path = approvedPlanPath.Replace(Path.DirectorySeparatorChar, '/'),
                    CandidateSizeBytes = 0,
                    CandidateSha256 = "",
                    RepoSizeBytes = 0,
                    RepoSha256 = ""
                });
            }
        }

        return changes.Count > 0 ? changes : new()
        {
            new PatchFileChange
            {
                Action = "A",
                Path = approvedPlanPath.Replace(Path.DirectorySeparatorChar, '/'),
                CandidateSizeBytes = 0,
                CandidateSha256 = "",
                RepoSizeBytes = 0,
                RepoSha256 = ""
            }
        };
    }
}
