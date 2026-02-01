using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GovernorCli.State;

public static class BacklogSaver
{
    public static void Save(string backlogYamlPath, BacklogFile model)
    {
        if (string.IsNullOrWhiteSpace(backlogYamlPath))
            throw new ArgumentException("backlogYamlPath is required.", nameof(backlogYamlPath));

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var yaml = serializer.Serialize(model);

        // Atomic-ish write:
        // write temp then replace to avoid corrupting file on crash.
        var dir = Path.GetDirectoryName(backlogYamlPath) ?? ".";
        Directory.CreateDirectory(dir);

        var tmp = Path.Combine(dir, $".backlog.{Guid.NewGuid():N}.tmp.yaml");
        File.WriteAllText(tmp, yaml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        // Replace if exists, else move into place
        if (File.Exists(backlogYamlPath))
            File.Delete(backlogYamlPath);

        File.Move(tmp, backlogYamlPath);
    }
}
