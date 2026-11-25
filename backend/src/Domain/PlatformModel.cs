namespace Iccc.Domain;

public enum TeamRole
{
    Viewer,
    Engineer,
    Approver,
    Owner,
    Auditor
}

public enum DeploymentModel
{
    VirtualMachines,
    Containers,
    Kubernetes,
    Other
}

public enum EnvironmentKind
{
    Development,
    Test,
    Staging,
    Production
}

public enum DependencyKind
{
    SqlServer,
    PostgreSql,
    Redis,
    MessageBroker,
    ExternalApi,
    ObjectStorage,
    Other
}

public enum WorkloadMetricKind
{
    RequestsPerSecond,
    ConcurrentUsers,
    RequestsPerMinute,
    RequestsPerHour
}

public enum LoadTestType
{
    Smoke,
    Load,
    Stress,
    Spike,
    Soak,
    Breakpoint
}

public enum ExecutionStatus
{
    Draft,
    AwaitingAuthorization,
    Authorized,
    Queued,
    Running,
    Cancelling,
    Cancelled,
    Succeeded,
    Failed,
    Rejected
}

public enum RequestStatus
{
    Draft,
    Submitted,
    Testing,
    Analysis,
    PendingApproval,
    Approved,
    Rejected
}

public enum ObservationSource
{
    LiveMeasurement,
    DemonstrationFixture
}

public sealed class UserAccount
{
    public Guid Id { get; set; }
    public string ExternalSubject { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsPlatformAdmin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Team
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public List<TeamMembership> Memberships { get; set; } = [];
}

public sealed class TeamMembership
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
    public TeamRole Role { get; set; }
    public UserAccount? User { get; set; }
    public Team? Team { get; set; }
}

public sealed class RegisteredService
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid OwnerUserId { get; set; }
    public string TechnologyStack { get; set; } = "";
    public string Runtime { get; set; } = "";
    public DeploymentModel DeploymentModel { get; set; }
    public string GrowthNotes { get; set; } = "";
    public string AuthenticationNotes { get; set; } = "";
    public string NetworkNotes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public Team? Team { get; set; }
    public List<ServiceEnvironment> Environments { get; set; } = [];
    public List<ServiceComponent> Components { get; set; } = [];
    public List<ServiceEndpoint> Endpoints { get; set; } = [];
    public List<ServiceDependency> Dependencies { get; set; } = [];
}

public sealed class ServiceEnvironment
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public string Name { get; set; } = "";
    public EnvironmentKind Kind { get; set; }
    public string BaseUrl { get; set; } = "";
    public bool LoadTestAllowed { get; set; }
    public string AllowlistHosts { get; set; } = "";
    public int MaxRequestsPerSecond { get; set; }
    public int MaxDurationSeconds { get; set; }
    public RegisteredService? Service { get; set; }
}

public sealed class ServiceComponent
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public int InstanceCount { get; set; }
    public decimal CpuMillicores { get; set; }
    public decimal MemoryMb { get; set; }
}

public sealed class ServiceEndpoint
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public string Method { get; set; } = "GET";
    public string Path { get; set; } = "/";
    public decimal TrafficWeightPercent { get; set; }
    public bool AuthenticationRequired { get; set; }
    public int AveragePayloadBytes { get; set; }
}

public sealed class ServiceDependency
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public string Name { get; set; } = "";
    public DependencyKind Kind { get; set; }
    public string Criticality { get; set; } = "required";
    public string Notes { get; set; } = "";
}

public sealed class WorkloadProfile
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public string Name { get; set; } = "";
    public WorkloadMetricKind Metric { get; set; }
    public decimal TargetValue { get; set; }
    public decimal? PeakValue { get; set; }
    public int? VirtualUsers { get; set; }
    public int RampUpSeconds { get; set; }
    public int SustainSeconds { get; set; }
    public int RampDownSeconds { get; set; }
    public decimal? BurstMultiplier { get; set; }
    public decimal ReadRatio { get; set; } = 1;
    public decimal? CacheHitRatio { get; set; }
    public int? AuthenticationOverheadMs { get; set; }
    public int? AveragePayloadBytes { get; set; }
    public string ScenarioNotes { get; set; } = "";
    public decimal AvailabilityTarget { get; set; }
    public decimal MaxErrorRate { get; set; }
    public decimal? MaxAverageLatencyMs { get; set; }
    public decimal? MaxP95LatencyMs { get; set; }
    public decimal? MaxP99LatencyMs { get; set; }
    public List<WorkloadEndpointShare> EndpointShares { get; set; } = [];
}

public sealed class WorkloadEndpointShare
{
    public Guid Id { get; set; }
    public Guid WorkloadProfileId { get; set; }
    public Guid ServiceEndpointId { get; set; }
    public decimal SharePercent { get; set; }
}

public sealed class LoadTestDefinition
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public Guid EnvironmentId { get; set; }
    public Guid WorkloadProfileId { get; set; }
    public string Engine { get; set; } = "k6";
    public LoadTestType TestType { get; set; }
    public string ScriptSource { get; set; } = "generated";
    public string ScriptBody { get; set; } = "";
    public string ScriptVersion { get; set; } = "1";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class LoadTestExecution
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public ExecutionStatus Status { get; set; }
    public Guid? AuthorizedByUserId { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public bool CancellationRequested { get; set; }
    public bool GeneratorSaturated { get; set; }
    public string EngineHandle { get; set; } = "";
    public string ConfigurationSnapshotJson { get; set; } = "";
    public ObservationSource Source { get; set; }
    public string FailureReason { get; set; } = "";
    public LoadTestResult? Result { get; set; }
}

public sealed class LoadTestResult
{
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public decimal AchievedRps { get; set; }
    public decimal ErrorRate { get; set; }
    public decimal? P50LatencyMs { get; set; }
    public decimal? P90LatencyMs { get; set; }
    public decimal? P95LatencyMs { get; set; }
    public decimal? P99LatencyMs { get; set; }
    public decimal? AverageLatencyMs { get; set; }
    public long RequestCount { get; set; }
    public bool SlaMet { get; set; }
    public decimal? SlaCompliantRps { get; set; }
    public string StatusCodesJson { get; set; } = "{}";
}

public sealed class TelemetrySource
{
    public Guid Id { get; set; }
    public Guid EnvironmentId { get; set; }
    public string Provider { get; set; } = "prometheus";
    public string Endpoint { get; set; } = "";
    public string ServiceLabel { get; set; } = "";
}

public sealed class TelemetrySnapshot
{
    public Guid Id { get; set; }
    public Guid ExecutionId { get; set; }
    public string Provider { get; set; } = "";
    public string MetricName { get; set; } = "";
    public string ComponentKind { get; set; } = "";
    public string LabelsJson { get; set; } = "{}";
    public decimal Value { get; set; }
    public string Unit { get; set; } = "";
    public DateTimeOffset ObservedAt { get; set; }
}

public sealed class CatalogResource
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string CatalogVersion { get; set; } = "";
    public decimal Vcpu { get; set; }
    public decimal MemoryGb { get; set; }
    public decimal StorageGb { get; set; }
    public string Currency { get; set; } = "USD";
    public List<CatalogPrice> Prices { get; set; } = [];
}

public sealed class CatalogPrice
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public string Component { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int DepreciationMonths { get; set; } = 36;
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
}

public sealed class CapacityCalculationRecord
{
    public Guid Id { get; set; }
    public Guid? ExecutionId { get; set; }
    public Guid ServiceId { get; set; }
    public string FormulaVersion { get; set; } = "";
    public string Status { get; set; } = "";
    public bool ApprovableAsValidated { get; set; }
    public string InputJson { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ScenarioComparisonRecord
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public string InputJson { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class InfrastructureRequest
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public Guid? WorkloadProfileId { get; set; }
    public Guid? CalculationId { get; set; }
    public Guid? ComparisonId { get; set; }
    public RequestStatus Status { get; set; }
    public bool RiskAccepted { get; set; }
    public string Title { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public List<ApprovalEvent> History { get; set; } = [];
}

public sealed class ApprovalEvent
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public RequestStatus FromStatus { get; set; }
    public RequestStatus ToStatus { get; set; }
    public Guid ActorUserId { get; set; }
    public string Note { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public sealed class AssistantConversation
{
    public Guid Id { get; set; }
    public Guid? ServiceId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<AssistantMessage> Messages { get; set; } = [];
}

public sealed class AssistantMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public string ToolTraceJson { get; set; } = "[]";
    public DateTimeOffset At { get; set; }
}

public sealed class AuditEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid? EntityId { get; set; }
    public string Details { get; set; } = "";
}
