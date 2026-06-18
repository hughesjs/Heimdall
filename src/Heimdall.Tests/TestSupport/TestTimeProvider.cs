namespace Heimdall.Tests.TestSupport;

/// <summary>A controllable <see cref="TimeProvider"/> for driving staleness logic deterministically.</summary>
internal sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
