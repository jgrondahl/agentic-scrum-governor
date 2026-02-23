using GovernorCli.Application.UseCases;

namespace GovernorCli.Application.Stores;

/// <summary>
/// Abstraction for deliver use case to enable mocking in tests.
/// </summary>
public interface IDeliverUseCase
{
    /// <summary>
    /// Process a delivery request.
    /// </summary>
    DeliverResponse Process(DeliverRequest request);
}
