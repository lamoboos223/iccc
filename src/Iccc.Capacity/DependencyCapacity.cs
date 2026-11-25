namespace Iccc.Capacity;

internal static class DependencyCapacity
{
    public static void CollectBlockers(
        CapacityCalculationInput input,
        List<string> blockers,
        List<BottleneckFinding> bottlenecks)
    {
        var policy = input.Policy;
        var db = input.Database;
        if (db is not null)
        {
            if (db.CpuUtilization is decimal cpu && cpu > policy.MaxCpuUtilization)
            {
                Add(blockers, bottlenecks, "database", "DB-CPU", BottleneckSeverity.PolicyBreach,
                    $"Database CPU was {cpu:P0}, above the {policy.MaxCpuUtilization:P0} cap. More application instances will not remove this limit.");
            }

            if (db.LockWaitMs is decimal locks && locks > policy.MaxLockWaitMs)
            {
                Add(blockers, bottlenecks, "database", "DB-LOCKS", BottleneckSeverity.BlocksGrowth,
                    $"Database lock wait was {locks} ms, above {policy.MaxLockWaitMs} ms. This is not solved by adding application instances.");
            }

            if (db.DiskLatencyMs is decimal disk && disk > policy.MaxDiskLatencyMs)
            {
                Add(blockers, bottlenecks, "database", "DB-DISK", BottleneckSeverity.BlocksGrowth,
                    $"Database disk latency was {disk} ms, above {policy.MaxDiskLatencyMs} ms.");
            }
        }

        var cache = input.Cache;
        if (cache is not null)
        {
            var memoryPressure = cache.MaxMemoryMb is decimal max && max > 0 && cache.UsedMemoryMb is decimal used
                && used / max > policy.MaxMemoryUtilization;
            var evicting = cache.EvictionsPerSecond is decimal evictions && evictions > 0;
            if (memoryPressure || evicting)
            {
                Add(blockers, bottlenecks, "cache", "CACHE-MEMORY", BottleneckSeverity.BlocksGrowth,
                    evicting
                        ? "Redis evicted keys during the test. Application scale-out would increase cache pressure."
                        : "Redis memory was above the configured cap.");
            }
        }

        var workers = input.Workers;
        if (workers is not null
            && workers.BlocksRequestPath
            && workers.JobsArrivedPerSecond is decimal arrived
            && workers.JobsCompletedPerSecond is decimal completed
            && arrived > completed)
        {
            Add(blockers, bottlenecks, "worker", "WORKER-LAG", BottleneckSeverity.BlocksGrowth,
                "Background workers completed fewer jobs than arrived, and those jobs are on the request path.");
        }

        var broker = input.Broker;
        if (broker is not null
            && broker.BlocksRequestPath
            && broker.QueueDepthEnd is decimal end
            && broker.QueueDepthStart is decimal start
            && end > start * 1.1m
            && broker.ProcessedPerSecond is decimal processed
            && broker.IngressMessagesPerSecond is decimal ingress
            && processed < ingress)
        {
            Add(blockers, bottlenecks, "broker", "BROKER-LAG", BottleneckSeverity.BlocksGrowth,
                "The broker queue grew during the test while consumers ran slower than publishers.");
        }
    }

    public static void Contribute(
        CapacityCalculationInput input,
        decimal sustainableRps,
        List<CapacityLine> lines,
        List<string> limitations,
        List<string> assumptions)
    {
        ContributeDatabase(input, sustainableRps, lines, limitations, assumptions);
        ContributeCache(input, sustainableRps, lines, limitations, assumptions);
        ContributeBroker(input, lines, limitations, assumptions);
        ContributeWorkers(input, lines, limitations, assumptions);
        ContributeNetwork(input, sustainableRps, lines, limitations);
    }

    private static void ContributeDatabase(
        CapacityCalculationInput input,
        decimal sustainableRps,
        List<CapacityLine> lines,
        List<string> limitations,
        List<string> assumptions)
    {
        var db = input.Database;
        if (db is null)
        {
            return;
        }

        var target = input.Workload.TargetValue;
        var ratio = Ratio(target, sustainableRps);
        if (db.CpuUtilization is decimal cpu && db.Vcpu is decimal vcpu)
        {
            if (cpu <= input.Policy.MaxCpuUtilization && ratio <= 1)
            {
                lines.Add(CapacityLineFactory.Line(
                    "database", "vcpu", vcpu, "vcpu", EvidenceKind.Measured,
                    "DB-VCPU", "tested database vCPU stayed within the CPU cap at the measured rate",
                    [
                        new FormulaInput("testedVcpu", vcpu, "vcpu", EvidenceKind.Measured),
                        new FormulaInput("cpuUtilization", cpu, "ratio", EvidenceKind.Measured)
                    ],
                    ratio,
                    "This certifies the tested database size only at the measured rate."));
            }
            else if (cpu > input.Policy.MaxCpuUtilization && ratio <= 1)
            {
                var needed = vcpu * (cpu / input.Policy.MaxCpuUtilization);
                lines.Add(CapacityLineFactory.Line(
                    "database", "vcpu", needed, "vcpu", EvidenceKind.Extrapolated,
                    "DB-VCPU", "testedVcpu * observedCpu / cpuCap",
                    [
                        new FormulaInput("testedVcpu", vcpu, "vcpu", EvidenceKind.Measured),
                        new FormulaInput("cpuUtilization", cpu, "ratio", EvidenceKind.Measured)
                    ],
                    ratio,
                    "Linear CPU upsizing was not validated on a larger database."));
            }
            else
            {
                lines.Add(CapacityLineFactory.Line(
                    "database", "vcpu", null, "vcpu", EvidenceKind.Insufficient,
                    "DB-VCPU", "database size at an untested request rate is not estimated from a single point",
                    [new FormulaInput("ratio", ratio, "ratio", EvidenceKind.Calculated)],
                    ratio,
                    "Database CPU at the target rate was not measured.",
                    blocksApproval: true));
            }
        }

        if (db.ActiveConnections is decimal active)
        {
            if (ratio > input.Policy.HardExtrapolationRatio)
            {
                lines.Add(Insufficient("database", "connections", "DB-CONNECTIONS", "connection demand is outside the tested range"));
            }
            else
            {
                var estimated = active * ratio * input.Policy.HeadroomFactor;
                var evidence = ratio == 1 ? EvidenceKind.Calculated : EvidenceKind.Extrapolated;
                lines.Add(CapacityLineFactory.Line(
                    "database", "connections", ScalingMath.CeilToInt(estimated), "connections", evidence,
                    "DB-CONNECTIONS", "ceil(activeConnections * targetRps / sustainableRps * headroom)",
                    [
                        new FormulaInput("activeConnections", active, "connections", EvidenceKind.Measured),
                        new FormulaInput("ratio", ratio, "ratio", EvidenceKind.Calculated)
                    ],
                    ratio,
                    "Connection demand is scaled from one measured point and is not a pool measurement at the target rate."));
                if (db.MaxConnections is decimal max && estimated > max)
                {
                    limitations.Add($"Estimated connections {estimated:0} exceed the measured maximum of {max:0}.");
                }
            }
        }

        if (db.StorageGrowthGbPerDay is decimal growth && db.RetentionDays is int days && days > 0)
        {
            var projected = growth * days;
            var evidence = EvidenceKind.Calculated;
            if (input.Policy.GrowthProjectionFactor > 0)
            {
                projected *= 1 + input.Policy.GrowthProjectionFactor;
                evidence = EvidenceKind.Assumption;
                assumptions.Add($"Storage includes a {input.Policy.GrowthProjectionFactor:P0} growth projection that was not observed.");
            }

            lines.Add(CapacityLineFactory.Line(
                "database", "storage-growth-gb", projected, "GB", evidence,
                "DB-STORAGE", "dailyGrowthGb * retentionDays * (1 + growthProjection)",
                [
                    new FormulaInput("dailyGrowthGb", growth, "GB/day", EvidenceKind.Measured),
                    new FormulaInput("retentionDays", days, "days", EvidenceKind.Assumption)
                ],
                1,
                "Retention is a policy input. Growth is the observed daily rate."));
        }
        else
        {
            lines.Add(Insufficient("database", "storage-growth-gb", "DB-STORAGE", "Observed daily growth and a retention period are both required."));
            limitations.Add("Database storage growth was not estimated because growth or retention was missing.");
        }

        if (db.DiskIops is decimal iops)
        {
            AddScaled("database", "iops", iops, "iops", "DB-IOPS", "measuredIops * targetRps / sustainableRps", ratio, input, lines);
        }

        if (db.LogGrowthMbPerHour is decimal logGrowth)
        {
            AddScaled("database", "log-mb-per-hour", logGrowth, "MB/hour", "DB-LOG", "measuredLogGrowth * targetRps / sustainableRps", ratio, input, lines);
        }

        if (db.ReplicaCount > 1)
        {
            assumptions.Add($"Database replica count {db.ReplicaCount} is an availability assumption unless failover was part of the test.");
            lines.Add(CapacityLineFactory.Line(
                "database", "replicas", db.ReplicaCount, "nodes", EvidenceKind.Assumption,
                "DB-REPLICAS", "configured replica count",
                [new FormulaInput("replicas", db.ReplicaCount, "nodes", EvidenceKind.Assumption)],
                1,
                "Replication overhead was not derived from a failover test."));
        }
    }

    private static void ContributeCache(
        CapacityCalculationInput input,
        decimal sustainableRps,
        List<CapacityLine> lines,
        List<string> limitations,
        List<string> assumptions)
    {
        var cache = input.Cache;
        if (cache is null)
        {
            return;
        }

        if (!cache.WorkingSetFullyRepresented || cache.UsedMemoryMb is null)
        {
            lines.Add(Insufficient("cache", "memory-mb", "CACHE-MEMORY", "Cache memory is estimated only when the test represented the full working set and recorded used memory."));
            limitations.Add("Redis memory is not estimated. Key count and value size do not scale with request rate, and the working set was not fully represented.");
        }
        else
        {
            var used = cache.UsedMemoryMb.Value;
            var recommended = used / input.Policy.MaxMemoryUtilization;
            var aboveTested = cache.MaxMemoryMb is decimal max && recommended > max;
            var evidence = aboveTested ? EvidenceKind.Extrapolated : EvidenceKind.Calculated;
            lines.Add(CapacityLineFactory.Line(
                "cache", "memory-mb", recommended, "MB", evidence,
                "CACHE-MEMORY", "usedMemoryMb / memoryCap",
                [
                    new FormulaInput("usedMemoryMb", used, "MB", EvidenceKind.Measured),
                    new FormulaInput("memoryCap", input.Policy.MaxMemoryUtilization, "ratio", EvidenceKind.Assumption)
                ],
                1,
                aboveTested
                    ? "The memory figure is larger than the cache that was tested."
                    : "Memory follows the measured working set, not the request rate."));

            if (cache.ReplicaCount > 1)
            {
                assumptions.Add("Cache replica memory multiplies the measured working set and was not failover-tested.");
                lines.Add(CapacityLineFactory.Line(
                    "cache", "memory-mb-with-replicas", recommended * cache.ReplicaCount, "MB", EvidenceKind.Assumption,
                    "CACHE-REPLICAS", "recommendedMemoryMb * replicaCount",
                    [new FormulaInput("replicas", cache.ReplicaCount, "nodes", EvidenceKind.Assumption)],
                    1));
            }
        }

        if (cache.OperationsPerSecond is decimal ops)
        {
            var ratio = Ratio(input.Workload.TargetValue, sustainableRps);
            AddScaled("cache", "operations-per-second", ops, "ops", "CACHE-OPS", "measuredOps * targetRps / sustainableRps", ratio, input, lines);
        }
    }

    private static void ContributeBroker(
        CapacityCalculationInput input,
        List<CapacityLine> lines,
        List<string> limitations,
        List<string> assumptions)
    {
        var broker = input.Broker;
        if (broker is null)
        {
            return;
        }

        if (broker.NodeCount < 1 || broker.IngressMessagesPerSecond is not decimal ingress || ingress <= 0)
        {
            lines.Add(Insufficient("broker", "nodes", "BROKER-NODES", "Broker node count requires measured ingress and the tested node count."));
            return;
        }

        var cpuDerate = ScalingMath.Derate(broker.CpuUtilization, input.Policy.MaxCpuUtilization);
        if (input.Policy.RequireResourceUtilization && broker.CpuUtilization is null)
        {
            lines.Add(Insufficient("broker", "nodes", "BROKER-NODES", "Broker CPU utilization was not measured."));
            limitations.Add("Broker capacity was not estimated without CPU utilization.");
            return;
        }

        var perNode = ingress / broker.NodeCount * cpuDerate;
        var raw = perNode <= 0 ? 0 : ingress / perNode;
        var serving = Math.Max(ScalingMath.CeilToInt(raw * input.Policy.HeadroomFactor), broker.NodeCount);
        var evidence = serving > broker.NodeCount ? EvidenceKind.Extrapolated : EvidenceKind.Calculated;
        lines.Add(CapacityLineFactory.Line(
            "broker", "nodes", serving, "nodes", evidence,
            "BROKER-NODES", "ceil(ingress / ratedPerNode * headroom), not below the tested count",
            [
                new FormulaInput("ingress", ingress, "messages/s", EvidenceKind.Measured),
                new FormulaInput("testedNodes", broker.NodeCount, "nodes", EvidenceKind.Measured)
            ],
            1,
            serving > broker.NodeCount
                ? "The node count is above the tested count. Only the measured message rate was used."
                : "This sizes the broker for the measured message rate."));

        if (broker.IngressBytesPerSecond is decimal bytes && broker.RetentionHours is int hours)
        {
            var diskMb = bytes * hours * 3600m / (1024m * 1024m);
            lines.Add(CapacityLineFactory.Line(
                "broker", "retention-disk-mb", diskMb, "MB", EvidenceKind.Calculated,
                "BROKER-DISK", "ingressBytesPerSecond * retentionHours * 3600 / 1048576",
                [
                    new FormulaInput("ingressBytesPerSecond", bytes, "bytes/s", EvidenceKind.Measured),
                    new FormulaInput("retentionHours", hours, "hours", EvidenceKind.Assumption)
                ],
                1,
                "Retention duration is a policy input."));
            assumptions.Add("Broker disk uses the measured ingress rate held constant for the retention window.");
        }
    }

    private static void ContributeWorkers(
        CapacityCalculationInput input,
        List<CapacityLine> lines,
        List<string> limitations,
        List<string> assumptions)
    {
        var workers = input.Workers;
        if (workers is null)
        {
            return;
        }

        if (workers.WorkerCount < 1
            || workers.JobsCompletedPerSecond is not decimal completed
            || completed <= 0
            || workers.JobsArrivedPerSecond is not decimal arrived)
        {
            lines.Add(Insufficient("worker", "workers", "WORKER-COUNT", "Worker sizing needs the tested worker count, arrival rate, and completion rate."));
            limitations.Add("Worker capacity was not estimated because arrival or completion rate was missing.");
            return;
        }

        var derate = ScalingMath.Derate(workers.CpuUtilization, input.Policy.MaxCpuUtilization);
        var perWorker = completed / workers.WorkerCount * derate;
        var raw = perWorker <= 0 ? 0 : arrived / perWorker;
        var overhead = ScalingMath.OverheadMultiplier(raw, workers.WorkerCount, input.Policy.HorizontalScalingOverheadPerDoubling);
        var recommended = ScalingMath.CeilToInt(raw * overhead * input.Policy.HeadroomFactor);
        var evidence = recommended > workers.WorkerCount ? EvidenceKind.Extrapolated : EvidenceKind.Calculated;
        lines.Add(CapacityLineFactory.Line(
            "worker", "workers", recommended, "workers", evidence,
            "WORKER-COUNT", "ceil(arrivalRate / ratedCompletionsPerWorker * overhead * headroom)",
            [
                new FormulaInput("arrivalRate", arrived, "jobs/s", EvidenceKind.Measured),
                new FormulaInput("completedRate", completed, "jobs/s", EvidenceKind.Measured),
                new FormulaInput("testedWorkers", workers.WorkerCount, "workers", EvidenceKind.Measured)
            ],
            workers.WorkerCount == 0 ? 1 : (decimal)recommended / workers.WorkerCount,
            evidence == EvidenceKind.Extrapolated
                ? "The worker count is above the tested count and has not been retested."
                : null));
        if (input.Policy.HeadroomFactor != 1)
        {
            assumptions.Add("Worker headroom uses the same policy factor as application capacity.");
        }
    }

    private static void ContributeNetwork(
        CapacityCalculationInput input,
        decimal sustainableRps,
        List<CapacityLine> lines,
        List<string> limitations)
    {
        var network = input.Network;
        if (network is null)
        {
            return;
        }

        if (network.AverageRequestBytes is null || network.AverageResponseBytes is null)
        {
            lines.Add(Insufficient("network", "ingress-mbps", "NET-BANDWIDTH", "Average request and response sizes were not both measured."));
            limitations.Add("Network bandwidth was not estimated without measured payload sizes.");
            return;
        }

        var ratio = Ratio(input.Workload.TargetValue, sustainableRps);
        if (ratio > input.Policy.HardExtrapolationRatio)
        {
            lines.Add(Insufficient("network", "ingress-mbps", "NET-BANDWIDTH", "Target rate is outside the hard extrapolation limit."));
            return;
        }

        var evidence = ratio == 1 ? EvidenceKind.Calculated : EvidenceKind.Extrapolated;
        var ingress = input.Workload.TargetValue * network.AverageRequestBytes.Value * 8m / 1_000_000m;
        var egress = input.Workload.TargetValue * network.AverageResponseBytes.Value * 8m / 1_000_000m;
        lines.Add(CapacityLineFactory.Line(
            "network", "ingress-mbps", ingress, "Mbps", evidence,
            "NET-INGRESS", "targetRps * averageRequestBytes * 8 / 1000000",
            [new FormulaInput("averageRequestBytes", network.AverageRequestBytes.Value, "bytes", EvidenceKind.Measured)],
            ratio,
            "Byte sizes are measured. The rate uses the target throughput."));
        lines.Add(CapacityLineFactory.Line(
            "network", "egress-mbps", egress, "Mbps", evidence,
            "NET-EGRESS", "targetRps * averageResponseBytes * 8 / 1000000",
            [new FormulaInput("averageResponseBytes", network.AverageResponseBytes.Value, "bytes", EvidenceKind.Measured)],
            ratio));

        if (network.InternalBytesPerRequest is decimal internalBytes)
        {
            var eastWest = input.Workload.TargetValue * internalBytes * 8m / 1_000_000m;
            lines.Add(CapacityLineFactory.Line(
                "network", "east-west-mbps", eastWest, "Mbps", evidence,
                "NET-EAST-WEST", "targetRps * internalBytesPerRequest * 8 / 1000000",
                [new FormulaInput("internalBytesPerRequest", internalBytes, "bytes", EvidenceKind.Measured)],
                ratio));
        }
        else
        {
            limitations.Add("East-west service traffic was not measured and is omitted.");
        }
    }

    private static void AddScaled(
        string component,
        string metric,
        decimal measured,
        string unit,
        string formulaId,
        string formula,
        decimal ratio,
        CapacityCalculationInput input,
        List<CapacityLine> lines)
    {
        if (ratio > input.Policy.HardExtrapolationRatio)
        {
            lines.Add(Insufficient(component, metric, formulaId, "The target rate is outside the hard extrapolation limit."));
            return;
        }

        var evidence = ratio == 1 ? EvidenceKind.Calculated : EvidenceKind.Extrapolated;
        lines.Add(CapacityLineFactory.Line(
            component, metric, measured * ratio, unit, evidence, formulaId, formula,
            [
                new FormulaInput("measured", measured, unit, EvidenceKind.Measured),
                new FormulaInput("ratio", ratio, "ratio", EvidenceKind.Calculated)
            ],
            ratio,
            ratio == 1 ? null : "Scaled from the measured rate by the target-to-sustainable ratio."));
    }

    private static CapacityLine Insufficient(string component, string metric, string formulaId, string limitation)
    {
        return CapacityLineFactory.Line(
            component, metric, null, "n/a", EvidenceKind.Insufficient, formulaId, limitation, [], 1, limitation);
    }

    private static decimal Ratio(decimal target, decimal sustainable)
    {
        return sustainable <= 0 ? decimal.MaxValue : target / sustainable;
    }

    private static void Add(
        List<string> blockers,
        List<BottleneckFinding> bottlenecks,
        string component,
        string code,
        BottleneckSeverity severity,
        string explanation)
    {
        blockers.Add(explanation);
        bottlenecks.Add(new BottleneckFinding
        {
            Component = component,
            Code = code,
            Severity = severity,
            Explanation = explanation
        });
    }
}
