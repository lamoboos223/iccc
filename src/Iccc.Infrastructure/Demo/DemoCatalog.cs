using Iccc.Capacity;
using Iccc.Domain;

namespace Iccc.Infrastructure.Demo;

public static class DemoIds
{
    public static readonly Guid Team = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Engineer = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Approver = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Service = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid Environment = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid Workload = Guid.Parse("66666666-6666-6666-6666-666666666666");
    public static readonly Guid Execution = Guid.Parse("77777777-7777-7777-7777-777777777777");
    public static readonly Guid Calculation = Guid.Parse("88888888-8888-8888-8888-888888888888");
    public static readonly Guid Request = Guid.Parse("99999999-9999-9999-9999-999999999999");
    public static readonly Guid Sku = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
}

public static class DemoMeasurement
{
    public static CapacityCalculationInput Input { get; } = new()
    {
        Workload = new WorkloadTarget
        {
            Metric = WorkloadMetric.RequestsPerSecond,
            TargetValue = 10_000,
            AverageRequestsPerSecond = 2_000
        },
        Sla = new SlaTargets
        {
            MaxAverageLatencyMs = 200,
            MaxP95LatencyMs = 200,
            MaxErrorRate = 0.01m,
            AvailabilityTarget = 0.999m
        },
        Observation = new LoadObservation
        {
            AchievedRps = 2_500,
            ErrorRate = 0.002m,
            AverageLatencyMs = 45,
            P50LatencyMs = 40,
            P90LatencyMs = 70,
            P95LatencyMs = 95,
            P99LatencyMs = 140,
            GeneratorSaturated = false,
            SustainDuration = TimeSpan.FromMinutes(10),
            ObservedAtUtc = new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero)
        },
        Application = new ApplicationObservation
        {
            InstanceCount = 2,
            VcpuPerInstance = 4,
            MemoryGbPerInstance = 8,
            CpuUtilization = 0.62m,
            MemoryUtilization = 0.55m
        },
        Database = new DatabaseObservation
        {
            Vcpu = 8,
            MemoryGb = 32,
            CpuUtilization = 0.45m,
            MemoryUtilization = 0.50m,
            ActiveConnections = 40,
            MaxConnections = 200,
            QueryP95Ms = 12,
            LockWaitMs = 2,
            DiskIops = 500,
            DiskLatencyMs = 4,
            LogGrowthMbPerHour = 80,
            StorageUsedGb = 120,
            StorageGrowthGbPerDay = 2,
            RetentionDays = 30,
            CacheHitRatio = 0.97m
        },
        Cache = new CacheObservation
        {
            UsedMemoryMb = 1024,
            MaxMemoryMb = 2048,
            CpuUtilization = 0.20m,
            OperationsPerSecond = 8000,
            HitRatio = 0.93m,
            EvictionsPerSecond = 0,
            WorkingSetFullyRepresented = true
        },
        Network = new NetworkObservation
        {
            AverageRequestBytes = 800,
            AverageResponseBytes = 2400
        }
    };
}
