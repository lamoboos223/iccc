namespace Iccc.Application.Abstractions;

public interface ILoadTestEngine
{
    string EngineId { get; }
    Task<LoadEngineResult> ExecuteAsync(LoadEngineCommand command, CancellationToken cancellationToken);
}

public sealed record LoadEngineCommand
{
    public required Guid ExecutionId { get; init; }
    public required string ScriptBody { get; init; }
    public required string BaseUrl { get; init; }
    public string? AuthorizationToken { get; init; }
    public required int TargetRps { get; init; }
    public CancellationToken ExecutionStopped { get; init; }
}

public sealed record LoadEngineResult
{
    public required bool Completed { get; init; }
    public required bool GeneratorSaturated { get; init; }
    public decimal AchievedRps { get; init; }
    public decimal ErrorRate { get; init; }
    public decimal? P50LatencyMs { get; init; }
    public decimal? P90LatencyMs { get; init; }
    public decimal? P95LatencyMs { get; init; }
    public decimal? P99LatencyMs { get; init; }
    public string Detail { get; init; } = "";
}

public interface ITelemetryProvider
{
    string ProviderId { get; }
    Task<TelemetryQueryResult> QueryAsync(TelemetryQuery query, CancellationToken cancellationToken);
}

public sealed record TelemetryQuery(string Endpoint, string PromQl, DateTimeOffset From, DateTimeOffset To);

public sealed record TelemetryQueryResult(bool Succeeded, string Detail, IReadOnlyList<TelemetryPoint> Points);

public sealed record TelemetryPoint(string Metric, decimal Value, string LabelsJson);

public interface ILlmProvider
{
    string ProviderId { get; }
    Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}
