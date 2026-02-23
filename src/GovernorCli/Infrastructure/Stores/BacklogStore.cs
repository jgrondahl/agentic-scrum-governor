using GovernorCli.Application.Stores;
using GovernorCli.Domain.Exceptions;
using GovernorCli.State;

namespace GovernorCli.Infrastructure.Stores;

public class BacklogStore : IBacklogStore
{
    public BacklogFile Load(string filePath)
    {
        try
        {
            return BacklogLoader.Load(filePath);
        }
        catch (Exception ex)
        {
            throw new BacklogParseException(filePath, ex);
        }
    }

    public void Save(string filePath, BacklogFile backlog)
    {
        BacklogSaver.Save(filePath, backlog);
    }
}
