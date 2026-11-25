using Iccc.Application.Abstractions;
using Iccc.Domain;
using Iccc.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Iccc.Api;

public sealed class LoadTestWorker(
    IServiceScopeFactory scopes,
    ILogger<LoadTestWorker> logger,
    IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("LoadTesting:Worker", false))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Load-test worker failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<ILoadTestEngine>();
        var execution = await db.LoadTestExecutions.FirstOrDefaultAsync(item => item.Status == ExecutionStatus.Queued, stoppingToken);
        if (execution is null)
        {
            return;
        }

        execution.Status = ExecutionStatus.Running;
        execution.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(stoppingToken);
        var definition = await db.LoadTestDefinitions.SingleAsync(item => item.Id == execution.DefinitionId, stoppingToken);
        var environment = await db.Environments.SingleAsync(item => item.Id == definition.EnvironmentId, stoppingToken);
        var result = await engine.ExecuteAsync(new LoadEngineCommand
        {
            ExecutionId = execution.Id,
            ScriptBody = definition.ScriptBody,
            BaseUrl = environment.BaseUrl,
            TargetRps = 0
        }, stoppingToken);

        execution.FinishedAt = DateTimeOffset.UtcNow;
        execution.GeneratorSaturated = result.GeneratorSaturated;
        execution.Status = result.Completed && !result.GeneratorSaturated ? ExecutionStatus.Succeeded : ExecutionStatus.Failed;
        execution.FailureReason = result.Detail;
        if (result.Completed)
        {
            db.LoadTestResults.Add(new LoadTestResult
            {
                Id = Guid.NewGuid(),
                ExecutionId = execution.Id,
                AchievedRps = result.AchievedRps,
                ErrorRate = result.ErrorRate,
                P50LatencyMs = result.P50LatencyMs,
                P90LatencyMs = result.P90LatencyMs,
                P95LatencyMs = result.P95LatencyMs,
                P99LatencyMs = result.P99LatencyMs,
                SlaMet = false
            });
        }

        await db.SaveChangesAsync(stoppingToken);
    }
}
