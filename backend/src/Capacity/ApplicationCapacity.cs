namespace Iccc.Capacity;

internal static class ApplicationCapacity
{
    public static void Contribute(
        CapacityCalculationInput input,
        decimal sustainableRps,
        IReadOnlyList<string> blockers,
        List<CapacityLine> lines,
        List<string> limitations,
        List<string> assumptions)
    {
        var app = input.Application;
        if (app is null)
        {
            limitations.Add("No application observation was supplied, so instance, vCPU, and memory requirements are not estimated.");
            lines.Add(CapacityLineFactory.Line(
                "application", "serving-instances", null, "instances", EvidenceKind.Insufficient,
                "APP-INSTANCE-COUNT", "requires an application observation", [], 1,
                "Application telemetry was not supplied.", blocksApproval: true));
            return;
        }

        if (app.InstanceCount < 1 || app.VcpuPerInstance <= 0 || app.MemoryGbPerInstance <= 0)
        {
            limitations.Add("Application sizing requires a positive tested instance count, vCPU, and memory.");
            lines.Add(CapacityLineFactory.Line(
                "application", "serving-instances", null, "instances", EvidenceKind.Insufficient,
                "APP-INSTANCE-COUNT", "tested instance size must be recorded", [], 1,
                "Tested instance size was incomplete.", blocksApproval: true));
            return;
        }

        var perInstance = sustainableRps / app.InstanceCount;
        lines.Add(CapacityLineFactory.Line(
            "application", "tested-instances", app.InstanceCount, "instances", EvidenceKind.Measured,
            "APP-TESTED-COUNT", "instances present during the measurement",
            [new FormulaInput("instanceCount", app.InstanceCount, "instances", EvidenceKind.Measured)],
            1));
        lines.Add(CapacityLineFactory.Line(
            "application", "sustainable-rps", sustainableRps, "rps", EvidenceKind.Measured,
            "APP-SUSTAINABLE-RPS", "aggregate rate at which the SLA held and the generator was not saturated",
            [new FormulaInput("sustainableRps", sustainableRps, "rps", EvidenceKind.Measured)],
            1, blocksApproval: true));
        lines.Add(CapacityLineFactory.Line(
            "application", "sustainable-rps-per-instance", perInstance, "rps", EvidenceKind.Calculated,
            "APP-RPS-PER-INSTANCE", "sustainableRps / testedInstances",
            [
                new FormulaInput("sustainableRps", sustainableRps, "rps", EvidenceKind.Measured),
                new FormulaInput("testedInstances", app.InstanceCount, "instances", EvidenceKind.Measured)
            ],
            1));

        if (input.Policy.RequireResourceUtilization
            && (app.CpuUtilization is null || app.MemoryUtilization is null))
        {
            limitations.Add("CPU and memory utilization were not both measured, so a safe instance count is not estimated.");
            lines.Add(CapacityLineFactory.Line(
                "application", "serving-instances", null, "instances", EvidenceKind.Insufficient,
                "APP-UTILIZATION-REQUIRED", "cpu and memory utilization are required", [], 1,
                "Resource utilization was not measured.", blocksApproval: true));
            return;
        }

        var cpuDerate = ScalingMath.Derate(app.CpuUtilization, input.Policy.MaxCpuUtilization);
        var memoryDerate = ScalingMath.Derate(app.MemoryUtilization, input.Policy.MaxMemoryUtilization);
        var derate = Math.Min(cpuDerate, memoryDerate);
        var ratedPerInstance = perInstance * derate;
        if (derate < 1)
        {
            lines.Add(CapacityLineFactory.Line(
                "application", "utilization-derate", derate, "ratio", EvidenceKind.Calculated,
                "APP-UTIL-DERATE", "min(cpuCap / observedCpu, memoryCap / observedMemory), never above 1",
                [
                    new FormulaInput("cpuUtilization", app.CpuUtilization ?? 0, "ratio", EvidenceKind.Measured),
                    new FormulaInput("memoryUtilization", app.MemoryUtilization ?? 0, "ratio", EvidenceKind.Measured),
                    new FormulaInput("derate", derate, "ratio", EvidenceKind.Calculated)
                ],
                1,
                "Throughput per instance was reduced so the recommendation stays inside the utilization cap. Unused headroom is not converted into extra throughput."));
        }

        var target = input.Workload.TargetValue;
        var ratio = sustainableRps <= 0 ? decimal.MaxValue : target / sustainableRps;
        if (ratio > input.Policy.HardExtrapolationRatio)
        {
            limitations.Add(
                $"The target is {ratio:0.##}x the measured sustainable rate, above the hard extrapolation limit of {input.Policy.HardExtrapolationRatio:0.##}x. No instance recommendation is produced.");
            lines.Add(CapacityLineFactory.Line(
                "application", "serving-instances", null, "instances", EvidenceKind.Insufficient,
                "APP-EXTRAPOLATION-GUARD", "targetRps / sustainableRps <= hardExtrapolationRatio",
                [
                    new FormulaInput("targetRps", target, "rps", EvidenceKind.Assumption),
                    new FormulaInput("sustainableRps", sustainableRps, "rps", EvidenceKind.Measured),
                    new FormulaInput("ratio", ratio, "ratio", EvidenceKind.Calculated)
                ],
                ratio,
                "The target is outside the range this measurement can support.",
                blocksApproval: true));
            return;
        }

        if (blockers.Count > 0 && target > sustainableRps)
        {
            limitations.Add(
                "Additional application instances are not recommended because a dependency bottleneck was measured: "
                + string.Join(" ", blockers));
            lines.Add(CapacityLineFactory.Line(
                "application", "serving-instances", null, "instances", EvidenceKind.Insufficient,
                "APP-BOTTLENECK-GUARD", "application scale-out is withheld when a dependency is already past its cap",
                [new FormulaInput("targetRps", target, "rps", EvidenceKind.Assumption)],
                ratio,
                string.Join(" ", blockers),
                blocksApproval: true));
            return;
        }

        var raw = ratedPerInstance <= 0 ? 0 : target / ratedPerInstance;
        var overhead = ScalingMath.OverheadMultiplier(raw, app.InstanceCount, input.Policy.HorizontalScalingOverheadPerDoubling);
        var withHeadroom = raw * overhead * input.Policy.HeadroomFactor;
        var serving = ScalingMath.CeilToInt(withHeadroom);
        var beyondTested = serving > app.InstanceCount;
        var grewPastMeasurement = target > sustainableRps || (derate < 1 && beyondTested);
        var evidence = grewPastMeasurement ? EvidenceKind.Extrapolated : EvidenceKind.Calculated;
        string? limitation = null;
        if (target > sustainableRps)
        {
            limitation = $"The target exceeds measured sustainable throughput by {ratio:0.##}x. Horizontal scale-out assumes partial linearity and was not retested.";
            assumptions.Add("Horizontal scale-out uses a doubling overhead of "
                + input.Policy.HorizontalScalingOverheadPerDoubling.ToString("P0")
                + ". That overhead was not measured.");
        }
        else if (derate < 1 && beyondTested)
        {
            limitation = "Utilization was above the configured cap. Extra instances assume linear relief and were not tested.";
        }

        if (input.Policy.HeadroomFactor != 1)
        {
            assumptions.Add($"A headroom factor of {input.Policy.HeadroomFactor} was applied by policy. The extra capacity was not load-tested.");
        }

        lines.Add(CapacityLineFactory.Line(
            "application", "serving-instances", serving, "instances", evidence,
            "APP-INSTANCE-COUNT",
            "ceil(targetRps / ratedRpsPerInstance * overhead(doublings) * headroom)",
            [
                new FormulaInput("targetRps", target, "rps", target == sustainableRps ? EvidenceKind.Measured : EvidenceKind.Assumption),
                new FormulaInput("ratedRpsPerInstance", ratedPerInstance, "rps", EvidenceKind.Calculated),
                new FormulaInput("overhead", overhead, "ratio", EvidenceKind.Assumption),
                new FormulaInput("headroom", input.Policy.HeadroomFactor, "ratio", EvidenceKind.Assumption),
                new FormulaInput("servingInstances", serving, "instances", evidence)
            ],
            ratio,
            limitation,
            blocksApproval: true));

        lines.Add(CapacityLineFactory.Line(
            "application", "serving-vcpu", serving * app.VcpuPerInstance, "vcpu", evidence,
            "APP-VCPU-TOTAL", "servingInstances * testedVcpuPerInstance",
            [
                new FormulaInput("servingInstances", serving, "instances", evidence),
                new FormulaInput("vcpuPerInstance", app.VcpuPerInstance, "vcpu", EvidenceKind.Measured)
            ],
            ratio,
            "vCPU stays on the tested instance size. A larger SKU is not inferred."));

        lines.Add(CapacityLineFactory.Line(
            "application", "serving-memory-gb", serving * app.MemoryGbPerInstance, "GB", evidence,
            "APP-RAM-TOTAL", "servingInstances * testedMemoryGbPerInstance",
            [
                new FormulaInput("servingInstances", serving, "instances", evidence),
                new FormulaInput("memoryGbPerInstance", app.MemoryGbPerInstance, "GB", EvidenceKind.Measured)
            ],
            ratio,
            "Memory stays on the tested instance size. A larger SKU is not inferred."));

        var spare = input.Policy.Availability switch
        {
            AvailabilityMode.Single => 0,
            AvailabilityMode.NPlusOne => 1,
            AvailabilityMode.ActiveActive => serving,
            _ => 0
        };

        if (spare > 0)
        {
            assumptions.Add(input.Policy.Availability == AvailabilityMode.NPlusOne
                ? "N+1 adds one spare instance. A node failure was not tested."
                : "Active-active adds a second full serving set. Site failover was not tested.");
        }

        lines.Add(CapacityLineFactory.Line(
            "application", "availability-spare-instances", spare, "instances",
            spare == 0 ? EvidenceKind.Calculated : EvidenceKind.Assumption,
            "APP-HA-SPARE", "Single=0, N+1=1, ActiveActive=servingInstances",
            [new FormulaInput("availabilityMode", (int)input.Policy.Availability, "enum", EvidenceKind.Assumption)],
            1,
            spare == 0 ? null : "Spare capacity is an availability assumption, not a measured failover result."));

        lines.Add(CapacityLineFactory.Line(
            "application", "total-instances", serving + spare, "instances",
            spare == 0 ? evidence : EvidenceKind.Assumption,
            "APP-TOTAL-INSTANCES", "servingInstances + spareInstances",
            [
                new FormulaInput("servingInstances", serving, "instances", evidence),
                new FormulaInput("spareInstances", spare, "instances", spare == 0 ? EvidenceKind.Calculated : EvidenceKind.Assumption)
            ],
            ratio));
    }
}
