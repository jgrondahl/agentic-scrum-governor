using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GovernorCli.State;

public static class BacklogLoader
{
    public static BacklogFile Load(string backlogYamlPath)
    {
        if (!File.Exists(backlogYamlPath))
            throw new FileNotFoundException("Backlog file not found.", backlogYamlPath);

        var yaml = File.ReadAllText(backlogYamlPath);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var model = deserializer.Deserialize<BacklogFile>(yaml);
        model ??= new BacklogFile();
        model.Backlog ??= new List<BacklogItem>();
        return model;
    }
}
