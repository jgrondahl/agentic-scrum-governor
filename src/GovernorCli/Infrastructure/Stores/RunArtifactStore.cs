using GovernorCli.Application.Stores;
using GovernorCli.Runs;

namespace GovernorCli.Infrastructure.Stores;

public class RunArtifactStore : IRunArtifactStore
{
    public string CreateRunFolder(string baseDir, string runId)
    {
        return RunWriter.CreateRunFolder(baseDir, runId);
    }

    public void WriteJson(string runDir, string fileName, object payload)
    {
        RunWriter.WriteJson(runDir, fileName, payload);
    }

    public void WriteText(string runDir, string fileName, string content)
    {
        RunWriter.WriteText(runDir, fileName, content);
    }
}
