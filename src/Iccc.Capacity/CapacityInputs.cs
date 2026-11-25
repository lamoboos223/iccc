namespace Iccc.Capacity;

public sealed record CapacityPolicy
{
    public decimal MaxCpuUtilization { get; init; } = 0.70m;
    public decimal MaxMemoryUtilization { get; init; } = 0.75m;
    public decimal HeadroomFactor { get; init; } = 1.20m;
    public decimal HorizontalScalingOverheadPerDoubling { get; init; } = 0.05m;
    public AvailabilityMode Availability { get; init; } = AvailabilityMode.NPlusOne;
    public decimal MaxExtrapolationRatio { get; init; } = 2.0m;
    public decimal HardExtrapolationRatio { get; init; } = 5.0m;
    public int MinSustainSeconds { get; init; } = 60;
    public bool RequireResourceUtilization { get; init; } = true;
    public decimal MaxLockWaitMs { get; init; } = 50m;
    public decimal MaxDiskLatencyMs { get; init; } = 20m;
    public decimal GrowthProjectionFactor { get; init; }
    public decimal UnmeasuredSkuDerate { get; init; } = 0.80m;
}

public sealed record SlaTargets
{
    public decimal? MaxAverageLatencyMs { get; init; }
    public decimal? MaxP95LatencyMs { get; init; }
    public decimal? MaxP99LatencyMs { get; init; }
    public decimal MaxErrorRate { get; init; } = 0.01m;
    public decimal AvailabilityTarget { get; init; } = 0.999m;
}

public sealed record WorkloadTarget
{
    public required WorkloadMetric Metric { get; init; }
    public required decimal TargetValue { get; init; }
    public decimal? PeakValue { get; init; }
    public decimal? AverageRequestsPerSecond { get; init; }
}

public sealed record LoadObservation
{
    public required decimal AchievedRps { get; init; }
    public required decimal ErrorRate { get; init; }
    public decimal? AverageLatencyMs { get; init; }
    public decimal? P50LatencyMs { get; init; }
    public decimal? P90LatencyMs { get; init; }
    public decimal? P95LatencyMs { get; init; }
    public decimal? P99LatencyMs { get; init; }
    public required bool GeneratorSaturated { get; init; }
    public decimal? SlaCompliantRps { get; init; }
    public required TimeSpan SustainDuration { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed record ApplicationObservation
{
    public required int InstanceCount { get; init; }
    public required decimal VcpuPerInstance { get; init; }
    public required decimal MemoryGbPerInstance { get; init; }
    public decimal? CpuUtilization { get; init; }
    public decimal? MemoryUtilization { get; init; }
    public decimal? GcPauseMs { get; init; }
    public decimal? ThreadPoolUtilization { get; init; }
    public decimal? ActiveConnections { get; init; }
}

public sealed record DatabaseObservation
{
    public decimal? Vcpu { get; init; }
    public decimal? MemoryGb { get; init; }
    public decimal? CpuUtilization { get; init; }
    public decimal? MemoryUtilization { get; init; }
    public decimal? ActiveConnections { get; init; }
    public decimal? MaxConnections { get; init; }
    public decimal? QueryRps { get; init; }
    public decimal? QueryP95Ms { get; init; }
    public decimal? LockWaitMs { get; init; }
    public decimal? DiskIops { get; init; }
    public decimal? DiskLatencyMs { get; init; }
    public decimal? LogGrowthMbPerHour { get; init; }
    public decimal? StorageUsedGb { get; init; }
    public decimal? StorageGrowthGbPerDay { get; init; }
    public int? RetentionDays { get; init; }
    public decimal? CacheHitRatio { get; init; }
    public int ReplicaCount { get; init; } = 1;
}

public sealed record CacheObservation
{
    public decimal? UsedMemoryMb { get; init; }
    public decimal? MaxMemoryMb { get; init; }
    public decimal? CpuUtilization { get; init; }
    public decimal? OperationsPerSecond { get; init; }
    public decimal? HitRatio { get; init; }
    public decimal? EvictionsPerSecond { get; init; }
    public long? KeyCount { get; init; }
    public decimal? AverageValueBytes { get; init; }
    public bool WorkingSetFullyRepresented { get; init; }
    public int ReplicaCount { get; init; } = 1;
}

public sealed record BrokerObservation
{
    public int NodeCount { get; init; }
    public decimal? IngressMessagesPerSecond { get; init; }
    public decimal? ProcessedPerSecond { get; init; }
    public decimal? QueueDepthStart { get; init; }
    public decimal? QueueDepthEnd { get; init; }
    public decimal? CpuUtilization { get; init; }
    public decimal? MemoryMb { get; init; }
    public decimal? IngressBytesPerSecond { get; init; }
    public int? RetentionHours { get; init; }
    public bool BlocksRequestPath { get; init; }
}

public sealed record WorkerObservation
{
    public int WorkerCount { get; init; }
    public decimal? JobsArrivedPerSecond { get; init; }
    public decimal? JobsCompletedPerSecond { get; init; }
    public decimal? CpuUtilization { get; init; }
    public bool BlocksRequestPath { get; init; }
}

public sealed record NetworkObservation
{
    public decimal? AverageRequestBytes { get; init; }
    public decimal? AverageResponseBytes { get; init; }
    public decimal? InternalBytesPerRequest { get; init; }
}

public sealed record CapacityCalculationInput
{
    public required WorkloadTarget Workload { get; init; }
    public required SlaTargets Sla { get; init; }
    public required LoadObservation Observation { get; init; }
    public CapacityPolicy Policy { get; init; } = new();
    public ApplicationObservation? Application { get; init; }
    public DatabaseObservation? Database { get; init; }
    public CacheObservation? Cache { get; init; }
    public BrokerObservation? Broker { get; init; }
    public WorkerObservation? Workers { get; init; }
    public NetworkObservation? Network { get; init; }
}
