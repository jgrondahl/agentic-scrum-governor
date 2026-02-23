using GovernorCli.Application.Stores;
using GovernorCli.State;

namespace GovernorCli.Infrastructure.Stores;

public class DecisionStore : IDecisionStore
{
    public void LogDecision(string workdir, string entry)
    {
        DecisionLog.Append(workdir, entry);
    }
}
