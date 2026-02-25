using System.Text.Json;
using GovernorCli.Application.Models;
using GovernorCli.Application.Stores;

namespace GovernorCli.Infrastructure.Stores;

/// <summary>
/// Implementation of IPlanStore: persists and retrieves approved implementation plans.
/// Stores at: state/plans/item-{itemId}/implementation.plan.json
/// </summary>
public class PlanStore : IPlanStore
{
    public void SavePlan(string workdir, int itemId, ImplementationPlan plan)
    {
        if (string.IsNullOrWhiteSpace(workdir))
            throw new ArgumentException("workdir is required", nameof(workdir));

        var planPath = GetPlanPath(workdir, itemId);
        var dir = Path.GetDirectoryName(planPath) ?? ".";

        // Create directory structure
        Directory.CreateDirectory(dir);

        // Write plan as JSON (formatted)
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(plan, options);

        // Atomic-ish write: temp file then replace
        var tmp = Path.Combine(dir, $".implementation.plan.{Guid.NewGuid():N}.tmp.json");
        File.WriteAllText(tmp, json);

        if (File.Exists(planPath))
            File.Delete(planPath);

        File.Move(tmp, planPath);
    }

    public ImplementationPlan? LoadPlan(string workdir, int itemId)
    {
        var planPath = GetPlanPath(workdir, itemId);
        if (!File.Exists(planPath))
            return null;

        try
        {
            var json = File.ReadAllText(planPath);
            return JsonSerializer.Deserialize<ImplementationPlan>(json);
        }
        catch
        {
            return null;
        }
    }

    public string GetPlanPath(string workdir, int itemId)
    {
        return Path.Combine(workdir, "state", "plans", $"item-{itemId}", "implementation.plan.json");
    }
}
