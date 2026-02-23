using GovernorCli.Application.Stores;

namespace GovernorCli.Infrastructure.Stores;

public class EpicStore : IEpicStore
{
    public string ResolveAppId(string workdir, string epicId)
    {
        var epicsPath = Path.Combine(workdir, "state", "epics.yaml");
        if (!File.Exists(epicsPath))
            throw new FileNotFoundException($"Epic registry not found: {epicsPath}");

        var content = File.ReadAllText(epicsPath);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Simple YAML parser for epics.yaml list format:
        // epics:
        //   - id: epic-1
        //     app_id: myapp
        //   - id: epic-2
        //     app_id: otherapp

        var inEpicsSection = false;
        var currentEpicId = "";

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed == "epics:")
            {
                inEpicsSection = true;
                continue;
            }

            if (!inEpicsSection)
                continue;

            // List item start: "- id: <value>"
            if (trimmed.StartsWith("- id:"))
            {
                currentEpicId = trimmed.Substring("- id:".Length).Trim();
            }
            else if (currentEpicId == epicId && trimmed.StartsWith("app_id:"))
            {
                var appId = trimmed.Substring("app_id:".Length).Trim();
                return appId;
            }
        }

        throw new KeyNotFoundException($"Epic not found in registry: {epicId}");
    }
}
