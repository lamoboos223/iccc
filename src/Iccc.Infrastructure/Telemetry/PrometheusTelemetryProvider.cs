using System.Text.Json;
using Iccc.Application.Abstractions;

namespace Iccc.Infrastructure.Telemetry;

public sealed class PrometheusTelemetryProvider(HttpClient httpClient) : ITelemetryProvider
{
    public string ProviderId => "prometheus";

    public async Task<TelemetryQueryResult> QueryAsync(TelemetryQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Endpoint) || string.IsNullOrWhiteSpace(query.PromQl))
        {
            return new TelemetryQueryResult(false, "A Prometheus endpoint and query are required.", []);
        }

        var address = query.Endpoint.TrimEnd('/') + "/api/v1/query?query=" + Uri.EscapeDataString(query.PromQl);
        try
        {
            using var response = await httpClient.GetAsync(address, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new TelemetryQueryResult(false, "Prometheus returned " + (int)response.StatusCode + ".", []);
            }

            var points = Parse(body);
            return new TelemetryQueryResult(true, points.Count == 0 ? "Prometheus returned no samples for the test window." : "ok", points);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new TelemetryQueryResult(false, "Prometheus could not be queried: " + exception.Message, []);
        }
    }

    public static IReadOnlyList<TelemetryPoint> Parse(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var points = new List<TelemetryPoint>();
        foreach (var item in result.EnumerateArray())
        {
            if (!item.TryGetProperty("value", out var value) || value.GetArrayLength() < 2)
            {
                continue;
            }

            var raw = value[1].GetString();
            if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                continue;
            }

            var metric = item.TryGetProperty("metric", out var labels) ? labels.ToString() : "{}";
            var name = item.TryGetProperty("metric", out var metricElement) && metricElement.TryGetProperty("__name__", out var metricName)
                ? metricName.GetString() ?? "sample"
                : "sample";
            points.Add(new TelemetryPoint(name, number, metric));
        }

        return points;
    }
}

public sealed class DisabledLlmProvider : ILlmProvider
{
    public string ProviderId => "none";

    public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(null);
    }
}
