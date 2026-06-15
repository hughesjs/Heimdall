using Heimdall.Core.Auth;
using Heimdall.ViewModels;
using Shouldly;

namespace Heimdall.UiTests;

public class DeviceFlowViewModelTests
{
    [Fact]
    public async Task Successful_flow_surfaces_the_code_then_reports_authorised()
    {
        var coordinator = new AuthCoordinator(new InMemoryTokenStore(), new ScriptedAuthenticator("gho_token"));
        var viewModel = new DeviceFlowViewModel(coordinator);

        var token = await viewModel.AuthenticateAsync(default);

        token.ShouldBe("gho_token");
        viewModel.UserCode.ShouldBe("WXYZ-1234");
        viewModel.VerificationUri.ShouldBe("https://github.com/login/device");
        viewModel.State.ShouldBe(DeviceFlowState.Authorised);
    }

    [Fact]
    public async Task Denied_flow_returns_null_and_reports_failed()
    {
        var coordinator = new AuthCoordinator(new InMemoryTokenStore(), new DenyingAuthenticator());
        var viewModel = new DeviceFlowViewModel(coordinator);

        var token = await viewModel.AuthenticateAsync(default);

        token.ShouldBeNull();
        viewModel.State.ShouldBe(DeviceFlowState.Failed);
    }
}
