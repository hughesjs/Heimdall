using Heimdall.Core.Auth;
using Heimdall.Tests.TestSupport;
using Shouldly;

namespace Heimdall.Tests.Auth;

public class AuthCoordinatorTests
{
    [Fact]
    public async Task Returns_the_stored_token_without_running_the_flow()
    {
        var coordinator = new AuthCoordinator(new InMemoryTokenStore("existing"), new ThrowingAuthenticator());

        var token = await coordinator.GetOrAuthenticateAsync(_ => Task.CompletedTask, default);

        token.ShouldBe("existing");
    }

    [Fact]
    public async Task Runs_the_flow_and_saves_the_token_when_none_is_stored()
    {
        var store = new InMemoryTokenStore();
        var coordinator = new AuthCoordinator(store, new ScriptedAuthenticator("gho_new"));
        DeviceCodeResponse? shownToUser = null;

        var token = await coordinator.GetOrAuthenticateAsync(code =>
        {
            shownToUser = code;
            return Task.CompletedTask;
        }, default);

        token.ShouldBe("gho_new");
        (await store.GetTokenAsync()).ShouldBe("gho_new");
        shownToUser.ShouldNotBeNull();
        shownToUser.UserCode.ShouldBe("WXYZ-1234");
    }

    [Fact]
    public async Task Reauthenticate_clears_then_runs_the_flow()
    {
        var store = new InMemoryTokenStore("revoked");
        var coordinator = new AuthCoordinator(store, new ScriptedAuthenticator("gho_fresh"));

        var token = await coordinator.ReauthenticateAsync(_ => Task.CompletedTask, default);

        token.ShouldBe("gho_fresh");
        (await store.GetTokenAsync()).ShouldBe("gho_fresh");
    }
}
