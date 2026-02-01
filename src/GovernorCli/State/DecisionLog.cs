namespace GovernorCli.State;

public static class DecisionLog
{
    public static void Append(string workdir, string line)
    {
        var path = Path.Combine(workdir, "state", "decisions", "decision-log.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
            File.WriteAllText(path, "# Decision Log\n\n(append-only)\n\n");

        File.AppendAllText(path, line + Environment.NewLine);
    }
}
