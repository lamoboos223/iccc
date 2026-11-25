using System.Diagnostics;
using System.Text.Json;
using Iccc.Application.Abstractions;

namespace Iccc.Infrastructure.LoadTesting;

public sealed class K6LoadTestEngine(string k6Path, int singleGeneratorMaxRps) : ILoadTestEngine
{
    public string EngineId => "k6";

    public async Task<LoadEngineResult> ExecuteAsync(LoadEngineCommand command, CancellationToken cancellationToken)
    {
        if (command.TargetRps > singleGeneratorMaxRps)
        {
            return Failed(true, "The requested rate exceeds the single-generator ceiling. A distributed engine is required; this run was not started.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "iccc-k6", command.ExecutionId.ToString("N"));
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, "test.js");
        var summaryPath = Path.Combine(directory, "summary.json");
        await File.WriteAllTextAsync(scriptPath, command.ScriptBody, cancellationToken);

        var start = new ProcessStartInfo
        {
            FileName = k6Path,
            Arguments = $"run --summary-export \"{summaryPath}\" \"{scriptPath}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.Environment["ICCC_BASE_URL"] = command.BaseUrl;
        if (!string.IsNullOrEmpty(command.AuthorizationToken))
        {
            start.Environment["ICCC_AUTH_TOKEN"] = command.AuthorizationToken;
        }

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
            {
                return Failed(false, "k6 did not start.");
            }
        }
        catch (Exception exception)
        {
            return Failed(false, "k6 is not available: " + exception.Message);
        }

        await using var stop = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        });

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return Failed(false, "The run was cancelled.");
        }

        if (!File.Exists(summaryPath))
        {
            return Failed(false, "k6 did not write a summary. " + Trim(stderr));
        }

        return Parse(await File.ReadAllTextAsync(summaryPath, cancellationToken), stderr);
    }

    public static LoadEngineResult Parse(string summaryJson, string stderr)
    {
        using var document = JsonDocument.Parse(summaryJson);
        var metrics = document.RootElement.GetProperty("metrics");
        var dropped = MetricCount(metrics, "dropped_iterations");
        var achieved = MetricNamed(metrics, "http_reqs", "rate");
        var failed = MetricNamed(metrics, "http_req_failed", "rate");
        return new LoadEngineResult
        {
            Completed = achieved > 0,
            GeneratorSaturated = dropped > 0,
            AchievedRps = achieved,
            ErrorRate = failed,
            P50LatencyMs = MetricValue(metrics, "http_req_duration", "p(50)") ?? MetricValue(metrics, "http_req_duration", "med"),
            P90LatencyMs = MetricValue(metrics, "http_req_duration", "p(90)"),
            P95LatencyMs = MetricValue(metrics, "http_req_duration", "p(95)"),
            P99LatencyMs = MetricValue(metrics, "http_req_duration", "p(99)"),
            Detail = dropped > 0
                ? "k6 dropped iterations, so the generator was saturated and the service capacity is not accepted."
                : Trim(stderr)
        };
    }

    private static decimal MetricCount(JsonElement metrics, string name)
    {
        return metrics.TryGetProperty(name, out var metric)
            && metric.TryGetProperty("values", out var values)
            && values.TryGetProperty("count", out var count)
            ? count.GetDecimal()
            : 0;
    }

    private static decimal MetricNamed(JsonElement metrics, string name, string statistic)
    {
        return MetricValue(metrics, name, statistic) ?? 0;
    }

    private static decimal? MetricValue(JsonElement metrics, string name, string statistic)
    {
        if (metrics.TryGetProperty(name, out var metric)
            && metric.TryGetProperty("values", out var values)
            && values.TryGetProperty(statistic, out var value))
        {
            return value.GetDecimal();
        }

        return null;
    }

    private static LoadEngineResult Failed(bool saturated, string detail) => new()
    {
        Completed = false,
        GeneratorSaturated = saturated,
        Detail = detail
    };

    private static string Trim(string value) => value.Length <= 500 ? value : value[..500];
}
