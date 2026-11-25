namespace Iccc.Capacity;

public enum EvidenceKind
{
    Measured,
    Calculated,
    Extrapolated,
    Assumption,
    Insufficient
}

public enum CalculationStatus
{
    Sufficient,
    Partial,
    InsufficientEvidence,
    InvalidMeasurement
}

public enum AvailabilityMode
{
    Single,
    NPlusOne,
    ActiveActive
}

public enum WorkloadMetric
{
    RequestsPerSecond,
    ConcurrentUsers,
    RequestsPerMinute,
    RequestsPerHour
}

public enum BottleneckSeverity
{
    Informational,
    PolicyBreach,
    BlocksGrowth
}

public sealed record FormulaInput(string Name, decimal Value, string Unit, EvidenceKind Evidence);

public sealed record CapacityLine
{
    public required string Component { get; init; }
    public required string Metric { get; init; }
    public decimal? Value { get; init; }
    public required string Unit { get; init; }
    public required EvidenceKind Evidence { get; init; }
    public required string FormulaId { get; init; }
    public required string Formula { get; init; }
    public required IReadOnlyList<FormulaInput> Inputs { get; init; }
    public required decimal Confidence { get; init; }
    public string? Limitation { get; init; }
    public bool BlocksApproval { get; init; }
}

public sealed record BottleneckFinding
{
    public required string Component { get; init; }
    public required string Code { get; init; }
    public required BottleneckSeverity Severity { get; init; }
    public required string Explanation { get; init; }
}

public sealed record CapacityCalculationResult
{
    public required string FormulaVersion { get; init; }
    public required CalculationStatus Status { get; init; }
    public required bool ApprovableAsValidated { get; init; }
    public required IReadOnlyList<CapacityLine> Lines { get; init; }
    public required IReadOnlyList<BottleneckFinding> Bottlenecks { get; init; }
    public required IReadOnlyList<string> Limitations { get; init; }
    public required IReadOnlyList<string> Assumptions { get; init; }
}
