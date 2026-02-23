namespace GovernorCli.Domain.Enums;

/// <summary>
/// Standardized exit codes for all flow operations.
/// Used for CLI return codes and process exit status.
/// </summary>
public enum FlowExitCode
{
    Success = 0,
    InvalidRepoLayout = 2,
    ItemNotFound = 3,
    BacklogParseError = 4,
    PreconditionFailed = 5,           // Renamed from DefinitionOfReadyGateFailed for clarity
    PromptLoadError = 6,
    ContractValidationFailed = 7,
    ApplyFailed = 8,
    ValidationFailed = 9,
    UnexpectedError = 10
}
