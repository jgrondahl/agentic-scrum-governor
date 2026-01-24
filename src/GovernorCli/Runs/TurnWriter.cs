using System.Text.Json;

namespace GovernorCli.Runs;

public static class TurnWriter
{
    public static string EnsureTurnsDir(string runDir)
    {
        var turnsDir = Path.Combine(runDir, "turns");
        Directory.CreateDirectory(turnsDir);
        return turnsDir;
    }

    public static void WriteTurn(string turnsDir, int turnIndex, string personaId, object payload)
    {
        var file = $"{turnIndex:00}_{personaId}.json";
        var path = Path.Combine(turnsDir, file);

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
