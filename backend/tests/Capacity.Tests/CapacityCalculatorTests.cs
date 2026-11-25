using Iccc.Capacity;

namespace Iccc.Capacity.Tests;

public class CapacityCalculatorTests
{
    [Fact]
    public void Within_tested_rate_applies_headroom_and_n_plus_one_without_inventing_a_larger_sku()
    {
        var result = CapacityCalculator.Calculate(Sample(targetRps: 2000, headroom: 1.2m));

        Assert.Equal(CalculationStatus.Sufficient, result.Status);
        Assert.True(result.ApprovableAsValidated);
        Assert.Equal(3, Line(result, "serving-instances"));
        Assert.Equal(EvidenceKind.Calculated, Evidence(result, "serving-instances"));
        Assert.Equal(1, Line(result, "availability-spare-instances"));
        Assert.Equal(EvidenceKind.Assumption, Evidence(result, "availability-spare-instances"));
        Assert.Equal(4, Line(result, "total-instances"));
        Assert.Equal(6, Line(result, "serving-vcpu"));
        Assert.Equal(FormulaVersion(), result.FormulaVersion);
        Assert.Contains(result.Assumptions, item => item.Contains("N+1", StringComparison.Ordinal));
    }

    [Fact]
    public void Horizontal_scale_out_beyond_the_test_is_extrapolated_and_not_approvable_as_validated()
    {
        var result = CapacityCalculator.Calculate(Sample(targetRps: 8000));

        Assert.Equal(11, Line(result, "serving-instances"));
        Assert.Equal(EvidenceKind.Extrapolated, Evidence(result, "serving-instances"));
        Assert.False(result.ApprovableAsValidated);
        Assert.Contains("not retested", result.Lines.Single(line => line.Metric == "serving-instances").Limitation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cpu_above_the_cap_derates_throughput_and_does_not_treat_extra_instances_as_measured()
    {
        var result = CapacityCalculator.Calculate(Sample(targetRps: 2000, cpu: 0.80m));

        Assert.Equal(3, Line(result, "serving-instances"));
        Assert.Equal(EvidenceKind.Extrapolated, Evidence(result, "serving-instances"));
        Assert.False(result.ApprovableAsValidated);
    }

    [Fact]
    public void Database_saturation_blocks_application_scale_out()
    {
        var input = Sample(targetRps: 8000) with
        {
            Database = new DatabaseObservation
            {
                Vcpu = 8,
                CpuUtilization = 0.95m,
                LockWaitMs = 5,
                DiskLatencyMs = 4,
                StorageGrowthGbPerDay = 1,
                RetentionDays = 30
            }
        };

        var result = CapacityCalculator.Calculate(input);

        Assert.Equal(CalculationStatus.InsufficientEvidence, result.Status);
        Assert.Null(Value(result, "serving-instances"));
        Assert.Contains(result.Bottlenecks, item => item.Code == "DB-CPU");
        Assert.False(result.ApprovableAsValidated);
    }

    [Fact]
    public void Redis_memory_uses_the_working_set_and_does_not_scale_with_request_rate()
    {
        var input = Sample(targetRps: 2000) with
        {
            Cache = new CacheObservation
            {
                UsedMemoryMb = 1800,
                MaxMemoryMb = 2000,
                EvictionsPerSecond = 12,
                WorkingSetFullyRepresented = true,
                OperationsPerSecond = 5000
            }
        };

        var result = CapacityCalculator.Calculate(input);

        Assert.Equal(2400, Line(result, "memory-mb"));
        Assert.Equal(EvidenceKind.Extrapolated, Evidence(result, "memory-mb"));
        Assert.Contains(result.Bottlenecks, item => item.Code == "CACHE-MEMORY");
        Assert.DoesNotContain(result.Lines, line => line.Formula.Contains("rps", StringComparison.OrdinalIgnoreCase)
            && line.Metric == "memory-mb");
    }

    [Fact]
    public void Redis_memory_is_refused_when_the_working_set_was_not_represented()
    {
        var input = Sample(targetRps: 2000) with
        {
            Cache = new CacheObservation
            {
                UsedMemoryMb = 256,
                WorkingSetFullyRepresented = false
            }
        };

        var result = CapacityCalculator.Calculate(input);

        Assert.Null(Value(result, "memory-mb"));
        Assert.Equal(EvidenceKind.Insufficient, Evidence(result, "memory-mb"));
    }

    [Fact]
    public void Worker_shortfall_is_a_bottleneck_and_the_count_is_extrapolated()
    {
        var input = Sample(targetRps: 8000, headroom: 1m, availability: AvailabilityMode.Single) with
        {
            Workers = new WorkerObservation
            {
                WorkerCount = 4,
                JobsArrivedPerSecond = 100,
                JobsCompletedPerSecond = 40,
                CpuUtilization = 0.50m,
                BlocksRequestPath = true
            }
        };

        var result = CapacityCalculator.Calculate(input);

        Assert.Contains(result.Bottlenecks, item => item.Code == "WORKER-LAG");
        Assert.Null(Value(result, "serving-instances"));
        Assert.True(Line(result, "workers") > 4);
        Assert.Equal(EvidenceKind.Extrapolated, Evidence(result, "workers"));
    }

    [Fact]
    public void Target_beyond_the_hard_limit_produces_no_instance_recommendation()
    {
        var result = CapacityCalculator.Calculate(Sample(targetRps: 20000, achievedRps: 100, instances: 1));

        Assert.Equal(CalculationStatus.InsufficientEvidence, result.Status);
        Assert.Null(Value(result, "serving-instances"));
        Assert.Equal(100, Line(result, "sustainable-rps-per-instance"));
        Assert.False(result.ApprovableAsValidated);
    }

    [Fact]
    public void Latency_sla_failure_without_a_lower_point_does_not_invent_instances()
    {
        var result = CapacityCalculator.Calculate(Sample(targetRps: 2000, p95: 450, slaP95: 200));

        Assert.Equal(CalculationStatus.InsufficientEvidence, result.Status);
        Assert.DoesNotContain(result.Lines, line => line.Metric == "serving-instances");
        Assert.Contains(result.Limitations, item => item.Contains("violated the SLA", StringComparison.Ordinal));
    }

    [Fact]
    public void Saturated_generator_invalidates_the_measurement()
    {
        var result = CapacityCalculator.Calculate(Sample(targetRps: 2000, generatorSaturated: true));

        Assert.Equal(CalculationStatus.InvalidMeasurement, result.Status);
        Assert.Equal("GENERATOR-GATE", result.Lines[0].FormulaId);
        Assert.DoesNotContain(result.Lines, line => line.Metric == "serving-instances" && line.Value is not null);
    }

    [Fact]
    public void N_plus_one_adds_one_spare_when_headroom_is_disabled()
    {
        var result = CapacityCalculator.Calculate(Sample(
            targetRps: 4000,
            achievedRps: 4000,
            instances: 4,
            headroom: 1m,
            availability: AvailabilityMode.NPlusOne));

        Assert.Equal(4, Line(result, "serving-instances"));
        Assert.Equal(1, Line(result, "availability-spare-instances"));
        Assert.Equal(5, Line(result, "total-instances"));
        Assert.True(result.ApprovableAsValidated);
    }

    [Fact]
    public void Concurrent_users_are_not_converted_into_instances()
    {
        var input = Sample(targetRps: 2000) with
        {
            Workload = new WorkloadTarget
            {
                Metric = WorkloadMetric.ConcurrentUsers,
                TargetValue = 500
            }
        };

        var result = CapacityCalculator.Calculate(input);

        Assert.Equal(CalculationStatus.InsufficientEvidence, result.Status);
        Assert.Null(Value(result, "serving-instances"));
    }

    [Fact]
    public void Network_bandwidth_uses_measured_sizes_and_marks_a_higher_target_as_extrapolated()
    {
        var input = Sample(targetRps: 4000) with
        {
            Network = new NetworkObservation
            {
                AverageRequestBytes = 1000,
                AverageResponseBytes = 2000
            }
        };

        var result = CapacityCalculator.Calculate(input);

        Assert.Equal(32, Line(result, "ingress-mbps"));
        Assert.Equal(64, Line(result, "egress-mbps"));
        Assert.Equal(EvidenceKind.Extrapolated, Evidence(result, "ingress-mbps"));
    }

    private static string FormulaVersion() => CapacityCalculator.FormulaVersion;

    private static decimal Line(CapacityCalculationResult result, string metric) =>
        Value(result, metric) ?? throw new InvalidOperationException($"Missing {metric}");

    private static decimal? Value(CapacityCalculationResult result, string metric) =>
        result.Lines.Single(line => line.Metric == metric).Value;

    private static EvidenceKind Evidence(CapacityCalculationResult result, string metric) =>
        result.Lines.Single(line => line.Metric == metric).Evidence;

    private static CapacityCalculationInput Sample(
        decimal targetRps,
        decimal achievedRps = 2000,
        int instances = 2,
        decimal cpu = 0.50m,
        decimal memory = 0.40m,
        decimal p95 = 80,
        decimal slaP95 = 200,
        decimal headroom = 1.2m,
        AvailabilityMode availability = AvailabilityMode.NPlusOne,
        bool generatorSaturated = false)
    {
        return new CapacityCalculationInput
        {
            Workload = new WorkloadTarget
            {
                Metric = WorkloadMetric.RequestsPerSecond,
                TargetValue = targetRps,
                AverageRequestsPerSecond = targetRps
            },
            Sla = new SlaTargets
            {
                MaxP95LatencyMs = slaP95,
                MaxErrorRate = 0.01m,
                AvailabilityTarget = 0.999m
            },
            Observation = new LoadObservation
            {
                AchievedRps = achievedRps,
                ErrorRate = 0.001m,
                P95LatencyMs = p95,
                GeneratorSaturated = generatorSaturated,
                SustainDuration = TimeSpan.FromMinutes(10),
                ObservedAtUtc = new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero)
            },
            Policy = new CapacityPolicy
            {
                HeadroomFactor = headroom,
                Availability = availability,
                MaxExtrapolationRatio = 2,
                HardExtrapolationRatio = 5,
                HorizontalScalingOverheadPerDoubling = 0.05m,
                MinSustainSeconds = 60
            },
            Application = new ApplicationObservation
            {
                InstanceCount = instances,
                VcpuPerInstance = 2,
                MemoryGbPerInstance = 4,
                CpuUtilization = cpu,
                MemoryUtilization = memory
            }
        };
    }
}
