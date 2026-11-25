using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Iccc.Application.Approval;
using Iccc.Application.Assistant;
using Iccc.Application.LoadTesting;
using Iccc.Application.Reporting;
using Iccc.Application.Safety;
using Iccc.Capacity;
using Iccc.Costing;
using Iccc.Domain;
using Iccc.Infrastructure.Demo;
using Iccc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Iccc.Api;

public static class PlatformEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static void MapPlatform(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/dashboard", Dashboard);
        api.MapGet("/workload-presets", Presets);
        api.MapGet("/services", ListServices);
        api.MapGet("/services/{id:guid}", GetService);
        api.MapPost("/services", CreateService);
        api.MapPost("/services/{id:guid}/dependencies", AddDependency);
        api.MapPost("/services/{id:guid}/environments", AddEnvironment);
        api.MapPost("/services/{id:guid}/endpoints", AddEndpoint);
        api.MapGet("/services/{id:guid}/architecture", Architecture);
        api.MapPost("/workloads", CreateWorkload);
        api.MapPost("/load-tests/validate", ValidateLoadTest);
        api.MapPost("/load-tests/scripts", Script);
        api.MapPost("/capacity/calculations", Calculate);
        api.MapGet("/capacity/calculations/{id:guid}", GetCalculation);
        api.MapPost("/cost/estimates", Estimate);
        api.MapPost("/scenarios/optimize", Optimize);
        api.MapGet("/catalog", Catalog);
        api.MapPost("/requests", CreateRequest);
        api.MapGet("/requests", ListRequests);
        api.MapPost("/requests/{id:guid}/transition", Transition);
        api.MapGet("/requests/{id:guid}/report.md", (Guid id, PlatformDbContext db, CancellationToken ct) => Report(id, db, "md", ct));
        api.MapGet("/requests/{id:guid}/report.csv", (Guid id, PlatformDbContext db, CancellationToken ct) => Report(id, db, "csv", ct));
        api.MapGet("/requests/{id:guid}/report.html", (Guid id, PlatformDbContext db, CancellationToken ct) => Report(id, db, "html", ct));
        api.MapPost("/assistant/messages", Assistant);
    }

    private static async Task<IResult> Dashboard(PlatformDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var services = VisibleServices(db, user);
        return Results.Ok(new
        {
            services = await services.CountAsync(ct),
            pendingApprovals = await db.Requests.CountAsync(request => request.Status == RequestStatus.PendingApproval, ct),
            executions = await db.LoadTestExecutions.CountAsync(ct),
            validated = await db.Calculations.CountAsync(item => item.ApprovableAsValidated, ct),
            notValidated = await db.Calculations.CountAsync(item => !item.ApprovableAsValidated, ct)
        });
    }

    private static IResult Presets()
    {
        return Results.Ok(new[] { 1_000, 5_000, 10_000, 50_000, 100_000 }.Select(rps => new
        {
            name = $"{rps / 1000}K rps",
            metric = WorkloadMetricKind.RequestsPerSecond,
            targetValue = rps,
            note = "This preset is requests per second, not concurrent users."
        }));
    }

    private static async Task<IResult> ListServices(PlatformDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var items = await VisibleServices(db, user).Select(service => new
        {
            service.Id,
            service.Name,
            service.Slug,
            service.Runtime,
            service.DeploymentModel
        }).ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> GetService(Guid id, PlatformDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var service = await VisibleServices(db, user)
            .Include(item => item.Environments)
            .Include(item => item.Components)
            .Include(item => item.Endpoints)
            .Include(item => item.Dependencies)
            .FirstOrDefaultAsync(item => item.Id == id, ct);
        return service is null ? Results.NotFound() : Results.Ok(service);
    }

    private static async Task<IResult> CreateService(ServiceCreate body, PlatformDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var teams = user.FindAll("team").Select(claim => Guid.Parse(claim.Value)).ToArray();
        if (!CanWrite(user, body.TeamId) || (!user.IsInRole("PlatformAdmin") && !teams.Contains(body.TeamId)))
        {
            return Results.Json(new { error = "You cannot register a service for this team." }, statusCode: StatusCodes.Status403Forbidden);
        }

        var service = new RegisteredService
        {
            Id = Guid.NewGuid(),
            TeamId = body.TeamId,
            Name = body.Name,
            Slug = body.Slug,
            Description = body.Description,
            OwnerUserId = UserId(user),
            TechnologyStack = body.TechnologyStack,
            Runtime = body.Runtime,
            DeploymentModel = body.DeploymentModel,
            GrowthNotes = body.GrowthNotes ?? "",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Services.Add(service);
        await Audit(db, user, "service.create", "Service", service.Id, service.Name, ct);
        return Results.Created($"/api/services/{service.Id}", new { service.Id });
    }

    private static async Task<IResult> AddDependency(Guid id, DependencyCreate body, PlatformDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var service = await Writable(db, user, id, ct);
        if (service is null) return Results.NotFound();
        var dependency = new ServiceDependency
        {
            Id = Guid.NewGuid(),
            ServiceId = id,
            Name = body.Name,
            Kind = body.Kind,
            Criticality = body.Criticality ?? "required",
            Notes = body.Notes ?? ""
        };
        db.Dependencies.Add(dependency);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/services/{id}/dependencies/{dependency.Id}", new { dependency.Id });
    }

    private static async Task<IResult> AddEnvironment(Guid id, EnvironmentCreate body, PlatformDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var service = await Writable(db, user, id, ct);
        if (service is null) return Results.NotFound();
        if (body.Kind == EnvironmentKind.Production && body.LoadTestAllowed)
        {
            return Results.BadRequest(new { error = "Production cannot be approved for load tests." });
        }

        var environment = new ServiceEnvironment
        {
            Id = Guid.NewGuid(),
            ServiceId = id,
            Name = body.Name,
            Kind = body.Kind,
            BaseUrl = body.BaseUrl,
            LoadTestAllowed = body.LoadTestAllowed,
            AllowlistHosts = body.AllowlistHosts,
            MaxRequestsPerSecond = body.MaxRequestsPerSecond,
            MaxDurationSeconds = body.MaxDurationSeconds
        };
        db.Environments.Add(environment);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/services/{id}/environments/{environment.Id}", new { environment.Id });
    }

    private static async Task<IResult> AddEndpoint(Guid id, EndpointCreate body, PlatformDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var service = await Writable(db, user, id, ct);
        if (service is null) return Results.NotFound();
        var endpoint = new ServiceEndpoint
        {
            Id = Guid.NewGuid(),
            ServiceId = id,
            Method = body.Method,
            Path = body.Path,
            TrafficWeightPercent = body.TrafficWeightPercent,
            AuthenticationRequired = body.AuthenticationRequired,
            AveragePayloadBytes = body.AveragePayloadBytes
        };
        db.Endpoints.Add(endpoint);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { endpoint.Id });
    }

    private static async Task<IResult> Architecture(Guid id, PlatformDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var service = await VisibleServices(db, user).Include(item => item.Components).Include(item => item.Dependencies).Include(item => item.Endpoints)
            .FirstOrDefaultAsync(item => item.Id == id, ct);
        if (service is null) return Results.NotFound();
        return Results.Ok(new
        {
            service = service.Name,
            components = service.Components.Select(component => component.Name),
            endpoints = service.Endpoints.Select(endpoint => $"{endpoint.Method} {endpoint.Path}"),
            dependencies = service.Dependencies.Select(dependency => new { dependency.Name, dependency.Kind, dependency.Criticality })
        });
    }

    private static async Task<IResult> CreateWorkload(WorkloadCreate body, PlatformDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var service = await Writable(db, user, body.ServiceId, ct);
        if (service is null) return Results.NotFound();
        var errors = WorkloadRules.Validate(new WorkloadDraft
        {
            Metric = body.Metric,
            TargetValue = body.TargetValue,
            VirtualUsers = body.VirtualUsers,
            TargetRequestsPerSecond = body.Metric == WorkloadMetricKind.RequestsPerSecond ? body.TargetValue : body.TargetRequestsPerSecond,
            RampUpSeconds = body.RampUpSeconds,
            SustainSeconds = body.SustainSeconds,
            RampDownSeconds = body.RampDownSeconds,
            ReadRatio = body.ReadRatio,
            EndpointWeights = body.EndpointWeights
        });
        if (errors.Count > 0) return Results.BadRequest(new { errors });
        var profile = new WorkloadProfile
        {
            Id = Guid.NewGuid(),
            ServiceId = body.ServiceId,
            Name = body.Name,
            Metric = body.Metric,
            TargetValue = body.TargetValue,
            VirtualUsers = body.VirtualUsers,
            RampUpSeconds = body.RampUpSeconds,
            SustainSeconds = body.SustainSeconds,
            RampDownSeconds = body.RampDownSeconds,
            ReadRatio = body.ReadRatio,
            AvailabilityTarget = body.AvailabilityTarget,
            MaxErrorRate = body.MaxErrorRate,
            MaxAverageLatencyMs = body.MaxAverageLatencyMs,
            MaxP95LatencyMs = body.MaxP95LatencyMs,
            ScenarioNotes = "Requests per second and concurrent users are stored as separate fields."
        };
        db.Workloads.Add(profile);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/workloads/{profile.Id}", new { profile.Id });
    }

    private static IResult ValidateLoadTest(LoadTestGuardRequest body)
    {
        return Results.Ok(LoadTestGuard.Evaluate(body));
    }

    private static IResult Script(K6ScriptRequest body)
    {
        var script = K6ScriptGenerator.Generate(body);
        return Results.Ok(new { script, containsSecret = false });
    }

    private static async Task<IResult> Calculate(CalculationCreate body, PlatformDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var service = await Writable(db, user, body.ServiceId, ct);
        if (service is null) return Results.NotFound();
        var result = CapacityCalculator.Calculate(body.Input);
        var record = new CapacityCalculationRecord
        {
            Id = Guid.NewGuid(),
            ServiceId = body.ServiceId,
            ExecutionId = body.ExecutionId,
            FormulaVersion = result.FormulaVersion,
            Status = result.Status.ToString(),
            ApprovableAsValidated = result.ApprovableAsValidated,
            InputJson = JsonSerializer.Serialize(body.Input, Json),
            ResultJson = JsonSerializer.Serialize(result, Json),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Calculations.Add(record);
        await Audit(db, user, "capacity.calculate", "CapacityCalculation", record.Id, result.Status.ToString(), ct);
        return Results.Ok(new { record.Id, result });
    }

    private static async Task<IResult> GetCalculation(Guid id, PlatformDbContext db, CancellationToken ct)
    {
        var record = await db.Calculations.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (record is null) return Results.NotFound();
        var result = JsonSerializer.Deserialize<CapacityCalculationResult>(record.ResultJson, Json);
        return Results.Ok(new { record.Id, record.FormulaVersion, record.Status, record.ApprovableAsValidated, result, input = JsonDocument.Parse(record.InputJson) });
    }

    private static IResult Estimate(CostRequest body) => Results.Ok(CostCalculator.Calculate(body));

    private static async Task<IResult> Optimize(OptimizeCreate body, PlatformDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var result = ConfigurationOptimizer.Optimize(body.Request);
        var record = new ScenarioComparisonRecord
        {
            Id = Guid.NewGuid(),
            ServiceId = body.ServiceId,
            InputJson = JsonSerializer.Serialize(body.Request, Json),
            ResultJson = JsonSerializer.Serialize(result, Json),
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Comparisons.Add(record);
        await Audit(db, user, "scenario.optimize", "ScenarioComparison", record.Id, result.Selected?.Name ?? "none", ct);
        return Results.Ok(new { record.Id, result });
    }

    private static async Task<IResult> Catalog(PlatformDbContext db, CancellationToken ct)
    {
        var items = await db.CatalogResources.Include(resource => resource.Prices).ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateRequest(RequestCreate body, PlatformDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var service = await Writable(db, user, body.ServiceId, ct);
        if (service is null) return Results.NotFound();
        var request = new InfrastructureRequest
        {
            Id = Guid.NewGuid(),
            ServiceId = body.ServiceId,
            WorkloadProfileId = body.WorkloadProfileId,
            CalculationId = body.CalculationId,
            Status = RequestStatus.Draft,
            Title = body.Title,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Requests.Add(request);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/requests/{request.Id}", new { request.Id, request.Status });
    }

    private static async Task<IResult> ListRequests(PlatformDbContext db, CancellationToken ct)
    {
        var items = await db.Requests.Select(request => new { request.Id, request.Title, request.Status, request.ServiceId, request.CalculationId, request.RiskAccepted }).ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> Transition(Guid id, TransitionCreate body, PlatformDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var request = await db.Requests.Include(item => item.History).FirstOrDefaultAsync(item => item.Id == id, ct);
        if (request is null) return Results.NotFound();
        var calculation = request.CalculationId is Guid calculationId
            ? await db.Calculations.FirstOrDefaultAsync(item => item.Id == calculationId, ct)
            : null;
        var blocked = calculation is null || calculation.Status is nameof(CalculationStatus.InsufficientEvidence) or nameof(CalculationStatus.InvalidMeasurement);
        var allowed = ApprovalWorkflow.CanTransition(
            request.Status,
            body.To,
            CanApprove(user),
            calculation is not null,
            calculation?.ApprovableAsValidated ?? false,
            body.RiskAccepted,
            blocked);
        if (!allowed)
        {
            return Results.Conflict(new { error = "This transition is not allowed for the current evidence.", current = request.Status, calculationStatus = calculation?.Status, validated = calculation?.ApprovableAsValidated });
        }

        var from = request.Status;
        request.Status = body.To;
        request.RiskAccepted = body.RiskAccepted || request.RiskAccepted;
        db.ApprovalEvents.Add(new ApprovalEvent
        {
            Id = Guid.NewGuid(),
            RequestId = request.Id,
            FromStatus = from,
            ToStatus = body.To,
            ActorUserId = UserId(user),
            Note = body.Note ?? "",
            At = DateTimeOffset.UtcNow
        });
        await Audit(db, user, "request.transition", "InfrastructureRequest", request.Id, $"{from}->{body.To}", ct);
        return Results.Ok(new { request.Status });
    }

    private static async Task<IResult> Report(Guid id, PlatformDbContext db, string format, CancellationToken ct)
    {
        var request = await db.Requests.Include(item => item.History).FirstOrDefaultAsync(item => item.Id == id, ct);
        if (request?.CalculationId is not Guid calculationId) return Results.NotFound();
        var calculation = await db.Calculations.FirstAsync(item => item.Id == calculationId, ct);
        var service = await db.Services.Include(item => item.Dependencies).FirstAsync(item => item.Id == request.ServiceId, ct);
        var execution = calculation.ExecutionId is Guid executionId
            ? await db.LoadTestExecutions.FirstOrDefaultAsync(item => item.Id == executionId, ct)
            : null;
        var result = JsonSerializer.Deserialize<CapacityCalculationResult>(calculation.ResultJson, Json)
            ?? throw new InvalidOperationException("Stored calculation could not be read.");
        var document = new ReportDocument
        {
            ServiceName = service.Name,
            ArchitectureSummary = service.Runtime + " / " + service.DeploymentModel + ". Dependencies: " + string.Join(", ", service.Dependencies.Select(item => item.Name + " (" + item.Kind + ")")),
            WorkloadSummary = "Stored with the calculation input. The workload metric and the measurement are separate.",
            TestSummary = execution is null
                ? "No execution is linked."
                : $"Execution {execution.Id} status {execution.Status}, source {execution.Source}.",
            DemonstrationFixture = execution?.Source == ObservationSource.DemonstrationFixture,
            Calculation = result,
            ApprovalHistory = request.History.OrderBy(item => item.At).Select(item => $"{item.At:u} {item.FromStatus} -> {item.ToStatus}: {item.Note}").ToList()
        };
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            At = DateTimeOffset.UtcNow,
            Action = "report.generate",
            EntityType = "InfrastructureRequest",
            EntityId = request.Id,
            Details = format
        });
        await db.SaveChangesAsync(ct);
        return format switch
        {
            "csv" => Results.Text(InfrastructureReportBuilder.ToCsv(document), "text/csv"),
            "html" => Results.Text(InfrastructureReportBuilder.ToHtml(document), "text/html"),
            _ => Results.Text(InfrastructureReportBuilder.ToMarkdown(document), "text/markdown")
        };
    }

    private static async Task<IResult> Assistant(AssistantCreate body, PlatformDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var service = body.ServiceId is Guid serviceId
            ? await VisibleServices(db, user).Include(item => item.Dependencies).Include(item => item.Components).FirstOrDefaultAsync(item => item.Id == serviceId, ct)
            : null;
        var workload = service is null
            ? null
            : await db.Workloads.Where(item => item.ServiceId == service.Id).OrderByDescending(item => item.TargetValue).FirstOrDefaultAsync(ct);
        var calculation = service is null
            ? null
            : await db.Calculations.Where(item => item.ServiceId == service.Id).OrderByDescending(item => item.CreatedAt).FirstOrDefaultAsync(ct);
        var state = new AssistantState
        {
            ServiceId = body.ServiceId,
            ServiceFound = service is not null,
            ArchitectureComplete = service is not null && service.Dependencies.Count > 0 && service.Components.Count > 0,
            WorkloadDefined = workload is not null,
            WorkloadUsesRequestsPerSecond = workload?.Metric == WorkloadMetricKind.RequestsPerSecond,
            HasExecution = calculation is not null,
            MeasurementUsable = calculation is not null && calculation.Status != nameof(CalculationStatus.InvalidMeasurement),
            AuthorizeTests = body.AuthorizeTests
        };
        var plan = AssistantPlanner.Plan(state, body.Message);
        string message;
        if (plan.MayCalculate && calculation is not null)
        {
            var result = JsonSerializer.Deserialize<CapacityCalculationResult>(calculation.ResultJson, Json)!;
            var narrative = RecommendationNarrator.Explain(result);
            var guarded = NarrativeGuard.Apply(body.Message, RecommendationNarrator.Numbers(result));
            message = narrative + (guarded.Contains("removed", StringComparison.Ordinal)
                ? " The request included numbers that were not used as a recommendation."
                : "");
        }
        else
        {
            message = string.Join(" ", plan.Clarifications);
        }

        return Results.Ok(new
        {
            message,
            tools = plan.Tools,
            clarifications = plan.Clarifications,
            mayExecuteTest = plan.MayExecuteTest,
            mayCalculate = plan.MayCalculate,
            rule = "Numerical recommendations come from the calculation engine. The assistant does not invent capacity figures."
        });
    }

    private static IQueryable<RegisteredService> VisibleServices(PlatformDbContext db, ClaimsPrincipal user)
    {
        if (user.IsInRole("PlatformAdmin"))
        {
            return db.Services;
        }

        var teams = user.FindAll("team").Select(claim => Guid.Parse(claim.Value)).ToArray();
        return db.Services.Where(service => teams.Contains(service.TeamId));
    }

    private static async Task<RegisteredService?> Writable(PlatformDbContext db, ClaimsPrincipal user, Guid serviceId, CancellationToken ct)
    {
        var service = await VisibleServices(db, user).FirstOrDefaultAsync(item => item.Id == serviceId, ct);
        if (service is null || !CanWrite(user, service.TeamId))
        {
            return null;
        }

        return service;
    }

    private static bool CanWrite(ClaimsPrincipal user, Guid teamId) =>
        user.IsInRole("PlatformAdmin") || user.IsInRole("Engineer") || user.IsInRole("Owner");

    private static bool CanApprove(ClaimsPrincipal user) => user.IsInRole("PlatformAdmin") || user.IsInRole("Approver");

    private static Guid UserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("uid"), out var id) ? id : DemoIds.Engineer;

    private static async Task Audit(PlatformDbContext db, ClaimsPrincipal user, string action, string entityType, Guid entityId, string details, CancellationToken ct)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            At = DateTimeOffset.UtcNow,
            ActorUserId = UserId(user),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details
        });
        await db.SaveChangesAsync(ct);
    }
}

public sealed record ServiceCreate(Guid TeamId, string Name, string Slug, string Description, string TechnologyStack, string Runtime, DeploymentModel DeploymentModel, string? GrowthNotes);
public sealed record DependencyCreate(string Name, DependencyKind Kind, string? Criticality, string? Notes);
public sealed record EnvironmentCreate(string Name, EnvironmentKind Kind, string BaseUrl, bool LoadTestAllowed, string AllowlistHosts, int MaxRequestsPerSecond, int MaxDurationSeconds);
public sealed record EndpointCreate(string Method, string Path, decimal TrafficWeightPercent, bool AuthenticationRequired, int AveragePayloadBytes);
public sealed record WorkloadCreate(Guid ServiceId, string Name, WorkloadMetricKind Metric, decimal TargetValue, decimal? TargetRequestsPerSecond, int? VirtualUsers, int RampUpSeconds, int SustainSeconds, int RampDownSeconds, decimal ReadRatio, decimal AvailabilityTarget, decimal MaxErrorRate, decimal? MaxAverageLatencyMs, decimal? MaxP95LatencyMs, IReadOnlyList<decimal> EndpointWeights);
public sealed record CalculationCreate(Guid ServiceId, Guid? ExecutionId, CapacityCalculationInput Input);
public sealed record OptimizeCreate(Guid ServiceId, OptimizationRequest Request);
public sealed record RequestCreate(Guid ServiceId, Guid? WorkloadProfileId, Guid? CalculationId, string Title);
public sealed record TransitionCreate(RequestStatus To, bool RiskAccepted, string? Note);
public sealed record AssistantCreate(Guid? ServiceId, string Message, bool AuthorizeTests);
