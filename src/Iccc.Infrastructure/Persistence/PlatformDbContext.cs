using Iccc.Domain;
using Microsoft.EntityFrameworkCore;

namespace Iccc.Infrastructure.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMembership> Memberships => Set<TeamMembership>();
    public DbSet<RegisteredService> Services => Set<RegisteredService>();
    public DbSet<ServiceEnvironment> Environments => Set<ServiceEnvironment>();
    public DbSet<ServiceComponent> Components => Set<ServiceComponent>();
    public DbSet<ServiceEndpoint> Endpoints => Set<ServiceEndpoint>();
    public DbSet<ServiceDependency> Dependencies => Set<ServiceDependency>();
    public DbSet<WorkloadProfile> Workloads => Set<WorkloadProfile>();
    public DbSet<WorkloadEndpointShare> WorkloadShares => Set<WorkloadEndpointShare>();
    public DbSet<LoadTestDefinition> LoadTestDefinitions => Set<LoadTestDefinition>();
    public DbSet<LoadTestExecution> LoadTestExecutions => Set<LoadTestExecution>();
    public DbSet<LoadTestResult> LoadTestResults => Set<LoadTestResult>();
    public DbSet<TelemetrySource> TelemetrySources => Set<TelemetrySource>();
    public DbSet<TelemetrySnapshot> TelemetrySnapshots => Set<TelemetrySnapshot>();
    public DbSet<CatalogResource> CatalogResources => Set<CatalogResource>();
    public DbSet<CatalogPrice> CatalogPrices => Set<CatalogPrice>();
    public DbSet<CapacityCalculationRecord> Calculations => Set<CapacityCalculationRecord>();
    public DbSet<ScenarioComparisonRecord> Comparisons => Set<ScenarioComparisonRecord>();
    public DbSet<InfrastructureRequest> Requests => Set<InfrastructureRequest>();
    public DbSet<ApprovalEvent> ApprovalEvents => Set<ApprovalEvent>();
    public DbSet<AssistantConversation> Conversations => Set<AssistantConversation>();
    public DbSet<AssistantMessage> AssistantMessages => Set<AssistantMessage>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>().HasIndex(user => user.ExternalSubject).IsUnique();
        modelBuilder.Entity<Team>().HasIndex(team => team.Slug).IsUnique();
        modelBuilder.Entity<RegisteredService>().HasIndex(service => new { service.TeamId, service.Slug }).IsUnique();
        modelBuilder.Entity<RegisteredService>()
            .HasMany(service => service.Environments).WithOne(environment => environment.Service!).HasForeignKey(environment => environment.ServiceId);
        modelBuilder.Entity<RegisteredService>()
            .HasMany(service => service.Components).WithOne().HasForeignKey(component => component.ServiceId);
        modelBuilder.Entity<RegisteredService>()
            .HasMany(service => service.Endpoints).WithOne().HasForeignKey(endpoint => endpoint.ServiceId);
        modelBuilder.Entity<RegisteredService>()
            .HasMany(service => service.Dependencies).WithOne().HasForeignKey(dependency => dependency.ServiceId);
        modelBuilder.Entity<Team>()
            .HasMany(team => team.Memberships).WithOne(membership => membership.Team!).HasForeignKey(membership => membership.TeamId);
        modelBuilder.Entity<TeamMembership>()
            .HasOne(membership => membership.User).WithMany().HasForeignKey(membership => membership.UserId);
        modelBuilder.Entity<WorkloadProfile>()
            .HasMany(profile => profile.EndpointShares).WithOne().HasForeignKey(share => share.WorkloadProfileId);
        modelBuilder.Entity<LoadTestExecution>()
            .HasOne(execution => execution.Result).WithOne().HasForeignKey<LoadTestResult>(result => result.ExecutionId);
        modelBuilder.Entity<CatalogResource>()
            .HasMany(resource => resource.Prices).WithOne().HasForeignKey(price => price.ResourceId);
        modelBuilder.Entity<InfrastructureRequest>()
            .HasMany(request => request.History).WithOne().HasForeignKey(history => history.RequestId);
        modelBuilder.Entity<AssistantConversation>()
            .HasMany(conversation => conversation.Messages).WithOne().HasForeignKey(message => message.ConversationId);
        modelBuilder.Entity<LoadTestDefinition>().Property(definition => definition.ScriptBody).HasMaxLength(200_000);
        modelBuilder.Entity<CapacityCalculationRecord>().Property(record => record.InputJson).HasMaxLength(200_000);
        modelBuilder.Entity<CapacityCalculationRecord>().Property(record => record.ResultJson).HasMaxLength(200_000);
    }
}
