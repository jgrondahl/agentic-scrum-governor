using System.Text.Json;

namespace GovernorCli.Runs;

public static class RunWriter
{
    public static string CreateRunFolder(string stateRunsDir, string runId)
    {
        Directory.CreateDirectory(stateRunsDir);
        var runDir = Path.Combine(stateRunsDir, runId);
        Directory.CreateDirectory(runDir);
        return runDir;
    }

    public static void WriteJson(string runDir, string fileName, object payload)
    {
        var path = Path.Combine(runDir, fileName);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }

    public static void WriteText(string runDir, string fileName, string content)
    {
        var path = Path.Combine(runDir, fileName);
        File.WriteAllText(path, content);
    }
}
