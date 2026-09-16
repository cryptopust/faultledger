using FaultLedger.Application.Transfers;

namespace FaultLedger.Infrastructure;

public enum MockProviderScenario
{
    Success,
    Rejected,
    TimeoutBeforeAccept,
    TimeoutAfterAccept,
    ConnectionFailureBeforeAccept,
    ConnectionLostAfterAccept,
    Provider500BeforeAccept,
    Provider500AfterAccept,
    MalformedResponseAfterAccept,
    SlowResponse
}

public sealed record MockProviderScenarioDefinition(
    MockProviderScenario Scenario,
    bool RequestReachesProvider,
    bool ProviderAccepts,
    ProviderAcceptanceEvidence AcceptanceEvidence,
    ProviderFailureKind FailureKind,
    string SafeMessage);

public static class MockProviderScenarioCatalog
{
    public static MockProviderScenarioDefinition Get(MockProviderScenario scenario) => scenario switch
    {
        MockProviderScenario.Success => new(scenario, true, true, ProviderAcceptanceEvidence.ConfirmedAccepted,
            ProviderFailureKind.None, "The synthetic provider accepted the operation."),
        MockProviderScenario.Rejected => new(scenario, true, false, ProviderAcceptanceEvidence.ConfirmedRejected,
            ProviderFailureKind.Rejected, "The synthetic provider rejected the operation."),
        MockProviderScenario.TimeoutBeforeAccept => new(scenario, true, false, ProviderAcceptanceEvidence.DefinitelyNotAccepted,
            ProviderFailureKind.Timeout, "The synthetic provider timed out before acceptance."),
        MockProviderScenario.TimeoutAfterAccept => new(scenario, true, true, ProviderAcceptanceEvidence.AcceptanceAmbiguous,
            ProviderFailureKind.Timeout, "The synthetic provider accepted the operation, but its response timed out."),
        MockProviderScenario.ConnectionFailureBeforeAccept => new(scenario, false, false,
            ProviderAcceptanceEvidence.DefinitelyNotAccepted, ProviderFailureKind.ConnectionFailure,
            "The synthetic connection failed before provider acceptance."),
        MockProviderScenario.ConnectionLostAfterAccept => new(scenario, true, true, ProviderAcceptanceEvidence.AcceptanceAmbiguous,
            ProviderFailureKind.ConnectionFailure, "The provider accepted the operation before the connection was lost."),
        MockProviderScenario.Provider500BeforeAccept => new(scenario, true, false,
            ProviderAcceptanceEvidence.DefinitelyNotAccepted, ProviderFailureKind.ServerError,
            "The synthetic provider returned a server error before acceptance."),
        MockProviderScenario.Provider500AfterAccept => new(scenario, true, true, ProviderAcceptanceEvidence.AcceptanceAmbiguous,
            ProviderFailureKind.ServerError, "The provider accepted the operation before returning a server error."),
        MockProviderScenario.MalformedResponseAfterAccept => new(scenario, true, true,
            ProviderAcceptanceEvidence.AcceptanceAmbiguous, ProviderFailureKind.MalformedResponse,
            "The provider accepted the operation but returned an unusable response."),
        MockProviderScenario.SlowResponse => new(scenario, true, true, ProviderAcceptanceEvidence.ConfirmedAccepted,
            ProviderFailureKind.None, "The synthetic provider accepted the operation after a controlled delay."),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown mock provider scenario.")
    };
}
