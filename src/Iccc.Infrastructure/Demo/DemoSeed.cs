using System.Text.Json;
using System.Text.Json.Serialization;
using Iccc.Capacity;
using Iccc.Domain;
using Iccc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Iccc.Infrastructure.Demo;

public static class DemoSeed
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task SeedAsync(PlatformDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero);
        db.Users.AddRange(
            new UserAccount { Id = DemoIds.Engineer, ExternalSubject = "dev.engineer", Email = "engineer@example.com", DisplayName = "Dev Engineer", CreatedAt = now },
            new UserAccount { Id = DemoIds.Approver, ExternalSubject = "dev.approver", Email = "approver@example.com", DisplayName = "Dev Approver", CreatedAt = now });
        db.Teams.Add(new Team { Id = DemoIds.Team, Name = "Payments", Slug = "payments" });
        db.Memberships.AddRange(
            new TeamMembership { Id = Guid.NewGuid(), TeamId = DemoIds.Team, UserId = DemoIds.Engineer, Role = TeamRole.Engineer },
            new TeamMembership { Id = Guid.NewGuid(), TeamId = DemoIds.Team, UserId = DemoIds.Approver, Role = TeamRole.Approver });

        db.Services.Add(new RegisteredService
        {
            Id = DemoIds.Service,
            TeamId = DemoIds.Team,
            Name = "Payments API",
            Slug = "payments-api",
            Description = ".NET API with SQL Server and Redis. Demonstration data, not a live certification.",
            OwnerUserId = DemoIds.Engineer,
            TechnologyStack = ".NET",
            Runtime = ".NET 10",
            DeploymentModel = DeploymentModel.Containers,
            GrowthNotes = "Target 10K rps. Average traffic is lower than the peak target.",
            AuthenticationNotes = "Bearer token supplied to the load generator as an environment variable.",
            NetworkNotes = "Test environment only.",
            CreatedAt = now,
            Environments =
            [
                new ServiceEnvironment
                {
                    Id = DemoIds.Environment,
                    Name = "dedicated-test",
                    Kind = EnvironmentKind.Test,
                    BaseUrl = "https://payments-test.internal",
                    LoadTestAllowed = true,
                    AllowlistHosts = "payments-test.internal",
                    MaxRequestsPerSecond = 20000,
                    MaxDurationSeconds = 1800
                }
            ],
            Components =
            [
                new ServiceComponent
                {
                    Id = Guid.NewGuid(),
                    Name = "api",
                    Kind = "Api",
                    InstanceCount = 2,
                    CpuMillicores = 4000,
                    MemoryMb = 8192
                }
            ],
            Endpoints =
            [
                new ServiceEndpoint { Id = Guid.NewGuid(), Method = "GET", Path = "/payments/{id}", TrafficWeightPercent = 70, AveragePayloadBytes = 400 },
                new ServiceEndpoint { Id = Guid.NewGuid(), Method = "POST", Path = "/payments", TrafficWeightPercent = 30, AuthenticationRequired = true, AveragePayloadBytes = 1200 }
            ],
            Dependencies =
            [
                new ServiceDependency { Id = Guid.NewGuid(), Name = "Payments SQL", Kind = DependencyKind.SqlServer, Criticality = "required" },
                new ServiceDependency { Id = Guid.NewGuid(), Name = "Payments cache", Kind = DependencyKind.Redis, Criticality = "required" }
            ]
        });

        db.Workloads.Add(new WorkloadProfile
        {
            Id = DemoIds.Workload,
            ServiceId = DemoIds.Service,
            Name = "10K rps peak",
            Metric = WorkloadMetricKind.RequestsPerSecond,
            TargetValue = 10_000,
            PeakValue = 10_000,
            VirtualUsers = 400,
            RampUpSeconds = 120,
            SustainSeconds = 600,
            RampDownSeconds = 60,
            ReadRatio = 0.7m,
            CacheHitRatio = 0.93m,
            AveragePayloadBytes = 800,
            ScenarioNotes = "Virtual users are the k6 pool. The sizing metric is 10K requests per second.",
            AvailabilityTarget = 0.999m,
            MaxErrorRate = 0.01m,
            MaxAverageLatencyMs = 200,
            MaxP95LatencyMs = 200
        });

        var definitionId = Guid.NewGuid();
        db.LoadTestDefinitions.Add(new LoadTestDefinition
        {
            Id = definitionId,
            ServiceId = DemoIds.Service,
            EnvironmentId = DemoIds.Environment,
            WorkloadProfileId = DemoIds.Workload,
            Engine = "fixture",
            TestType = LoadTestType.Load,
            ScriptSource = "fixture",
            ScriptBody = "// Demonstration fixture. No live k6 run is implied.",
            ScriptVersion = "fixture-1",
            CreatedAt = now
        });
        db.LoadTestExecutions.Add(new LoadTestExecution
        {
            Id = DemoIds.Execution,
            DefinitionId = definitionId,
            Status = ExecutionStatus.Succeeded,
            AuthorizedByUserId = DemoIds.Engineer,
            AuthorizedAt = now,
            StartedAt = now,
            FinishedAt = now.AddMinutes(10),
            Source = ObservationSource.DemonstrationFixture,
            ConfigurationSnapshotJson = "{\"note\":\"fixture\"}"
        });
        db.LoadTestResults.Add(new LoadTestResult
        {
            Id = Guid.NewGuid(),
            ExecutionId = DemoIds.Execution,
            AchievedRps = 2500,
            ErrorRate = 0.002m,
            AverageLatencyMs = 45,
            P50LatencyMs = 40,
            P90LatencyMs = 70,
            P95LatencyMs = 95,
            P99LatencyMs = 140,
            RequestCount = 1_500_000,
            SlaMet = true
        });
        db.TelemetrySnapshots.AddRange(
            Snapshot("app_cpu_utilization", "application", 0.62m, "ratio"),
            Snapshot("app_memory_utilization", "application", 0.55m, "ratio"),
            Snapshot("db_cpu_utilization", "database", 0.45m, "ratio"),
            Snapshot("redis_memory_mb", "cache", 1024, "MB"));

        db.CatalogResources.Add(new CatalogResource
        {
            Id = DemoIds.Sku,
            Sku = "vm-4c8g",
            Name = "4 vCPU / 8 GB",
            Kind = "virtual-machine",
            CatalogVersion = "demo-2026.11",
            Vcpu = 4,
            MemoryGb = 8,
            Currency = "USD",
            Prices =
            [
                new CatalogPrice { Id = Guid.NewGuid(), Component = "Compute", Unit = "PerMonth", UnitPrice = 180, ValidFrom = new DateOnly(2026, 1, 1), ValidTo = new DateOnly(2026, 12, 31) },
                new CatalogPrice { Id = Guid.NewGuid(), Component = "License", Unit = "PerMonth", UnitPrice = 40 }
            ]
        });

        var calculation = CapacityCalculator.Calculate(DemoMeasurement.Input);
        db.Calculations.Add(new CapacityCalculationRecord
        {
            Id = DemoIds.Calculation,
            ExecutionId = DemoIds.Execution,
            ServiceId = DemoIds.Service,
            FormulaVersion = calculation.FormulaVersion,
            Status = calculation.Status.ToString(),
            ApprovableAsValidated = calculation.ApprovableAsValidated,
            InputJson = JsonSerializer.Serialize(DemoMeasurement.Input, Json),
            ResultJson = JsonSerializer.Serialize(calculation, Json),
            CreatedAt = now
        });
        db.Requests.Add(new InfrastructureRequest
        {
            Id = DemoIds.Request,
            ServiceId = DemoIds.Service,
            WorkloadProfileId = DemoIds.Workload,
            CalculationId = DemoIds.Calculation,
            Status = RequestStatus.PendingApproval,
            Title = "Payments API 10K rps",
            CreatedAt = now,
            History =
            [
                new ApprovalEvent { Id = Guid.NewGuid(), FromStatus = RequestStatus.Draft, ToStatus = RequestStatus.PendingApproval, ActorUserId = DemoIds.Engineer, Note = "Submitted with a demonstration fixture.", At = now }
            ]
        });
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            At = now,
            ActorUserId = DemoIds.Engineer,
            Action = "demo.seed",
            EntityType = "Service",
            EntityId = DemoIds.Service,
            Details = "Seeded a labeled demonstration fixture."
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static TelemetrySnapshot Snapshot(string metric, string component, decimal value, string unit) => new()
    {
        Id = Guid.NewGuid(),
        ExecutionId = DemoIds.Execution,
        Provider = "fixture",
        MetricName = metric,
        ComponentKind = component,
        Value = value,
        Unit = unit,
        ObservedAt = new DateTimeOffset(2026, 11, 25, 0, 5, 0, TimeSpan.Zero)
    };
}
