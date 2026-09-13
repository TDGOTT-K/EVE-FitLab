namespace EdenOS.Contracts.UseCases;

public enum UseCaseStatus
{
    Success,
    InvalidInput,
    PermissionDenied,
    NotFound,
    Conflict,
    DependencyUnavailable,
    Error
}

public sealed record UseCaseResult<T>
{
    public required UseCaseStatus Status { get; init; }

    public required string Summary { get; init; }

    public T? Data { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public required string TraceId { get; init; }

    public bool IsSuccess => Status == UseCaseStatus.Success;

    public static UseCaseResult<T> Success(
        T data,
        string summary,
        string traceId,
        IReadOnlyList<string>? warnings = null) =>
        new()
        {
            Status = UseCaseStatus.Success,
            Summary = summary,
            Data = data,
            Warnings = warnings ?? Array.Empty<string>(),
            Errors = Array.Empty<string>(),
            TraceId = traceId
        };

    public static UseCaseResult<T> Failure(
        UseCaseStatus status,
        string summary,
        string traceId,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<string>? warnings = null) =>
        new()
        {
            Status = status,
            Summary = summary,
            Data = default,
            Warnings = warnings ?? Array.Empty<string>(),
            Errors = errors ?? Array.Empty<string>(),
            TraceId = traceId
        };
}
