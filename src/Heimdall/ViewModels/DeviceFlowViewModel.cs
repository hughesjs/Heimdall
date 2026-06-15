using CommunityToolkit.Mvvm.ComponentModel;
using Heimdall.Core.Auth;

namespace Heimdall.ViewModels;

public enum DeviceFlowState
{
    Idle,
    AwaitingAuthorisation,
    Authorised,
    Failed
}

/// <summary>
/// Drives the onboarding view through the device flow: request a code, show it, wait for the user to
/// authorise, and surface the outcome. The token is returned to the caller (the orchestrator stores it).
/// </summary>
public sealed partial class DeviceFlowViewModel(AuthCoordinator coordinator) : ViewModelBase
{
    [ObservableProperty]
    private DeviceFlowState _state = DeviceFlowState.Idle;

    [ObservableProperty]
    private string _userCode = string.Empty;

    [ObservableProperty]
    private string _verificationUri = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Runs the flow, returning the token on success or null if the user denied / it expired.</summary>
    public async Task<string?> AuthenticateAsync(CancellationToken cancellationToken)
    {
        StatusMessage = "Requesting a device code…";
        try
        {
            var token = await coordinator.AuthenticateAsync(code =>
            {
                UserCode = code.UserCode;
                VerificationUri = code.VerificationUri;
                State = DeviceFlowState.AwaitingAuthorisation;
                StatusMessage = "Enter the code at the URL, then authorise Heimdall.";
                return Task.CompletedTask;
            }, cancellationToken);

            State = DeviceFlowState.Authorised;
            StatusMessage = "Authorised.";
            return token;
        }
        catch (DeviceFlowException exception)
        {
            State = DeviceFlowState.Failed;
            StatusMessage = $"Authorisation failed: {exception.Error}.";
            return null;
        }
    }
}
