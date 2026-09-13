namespace EdenOS.Application.Services;

public sealed class RuntimeSessionState(TimeProvider timeProvider)
{
    public DateTimeOffset StartedAtUtc { get; } = timeProvider.GetUtcNow();
}
