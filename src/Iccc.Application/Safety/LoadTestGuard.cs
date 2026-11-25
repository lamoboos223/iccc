using System.Text.RegularExpressions;
using Iccc.Domain;

namespace Iccc.Application.Safety;

public sealed record LoadTestGuardRequest
{
    public required EnvironmentKind EnvironmentKind { get; init; }
    public required bool LoadTestAllowed { get; init; }
    public required string TargetUrl { get; init; }
    public required IReadOnlyList<string> AllowlistHosts { get; init; }
    public required int RequestsPerSecond { get; init; }
    public required int DurationSeconds { get; init; }
    public required int MaxRequestsPerSecond { get; init; }
    public required int MaxDurationSeconds { get; init; }
    public required int HighLoadAuthorizationThreshold { get; init; }
    public required bool ExplicitlyAuthorized { get; init; }
    public string? ScriptBody { get; init; }
}

public sealed record LoadTestGuardResult(bool Allowed, IReadOnlyList<string> Reasons);

public static partial class LoadTestGuard
{
    public static LoadTestGuardResult Evaluate(LoadTestGuardRequest request)
    {
        var reasons = new List<string>();
        if (request.EnvironmentKind == EnvironmentKind.Production)
        {
            reasons.Add("Production environments cannot be load-tested.");
        }

        if (!request.LoadTestAllowed)
        {
            reasons.Add("This environment is not marked as approved for load tests.");
        }

        if (!Uri.TryCreate(request.TargetUrl, UriKind.Absolute, out var uri) || uri.HostNameType == UriHostNameType.Unknown)
        {
            reasons.Add("The target URL is not absolute.");
        }
        else
        {
            if (IsDeniedHost(uri.Host))
            {
                reasons.Add($"The host {uri.Host} is blocked, including cloud metadata addresses.");
            }
            else if (!request.AllowlistHosts.Any(allowed => HostMatches(uri.Host, allowed)))
            {
                reasons.Add($"The host {uri.Host} is not on the environment allowlist.");
            }
        }

        if (request.RequestsPerSecond <= 0 || request.DurationSeconds <= 0)
        {
            reasons.Add("Rate and duration must be positive.");
        }

        if (request.RequestsPerSecond > request.MaxRequestsPerSecond)
        {
            reasons.Add($"The requested rate exceeds the environment maximum of {request.MaxRequestsPerSecond} rps.");
        }

        if (request.DurationSeconds > request.MaxDurationSeconds)
        {
            reasons.Add($"The duration exceeds the environment maximum of {request.MaxDurationSeconds} seconds.");
        }

        if (request.RequestsPerSecond >= request.HighLoadAuthorizationThreshold && !request.ExplicitlyAuthorized)
        {
            reasons.Add("This rate requires explicit authorization before it can run.");
        }

        if (!string.IsNullOrWhiteSpace(request.ScriptBody) && SecretPattern().IsMatch(request.ScriptBody))
        {
            reasons.Add("The script appears to contain an inline secret. Pass credentials through environment variables.");
        }

        return new LoadTestGuardResult(reasons.Count == 0, reasons);
    }

    private static bool IsDeniedHost(string host)
    {
        return host.Equals("169.254.169.254", StringComparison.OrdinalIgnoreCase)
            || host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".metadata.internal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HostMatches(string host, string allowed)
    {
        if (string.IsNullOrWhiteSpace(allowed))
        {
            return false;
        }

        var rule = allowed.Trim();
        if (rule.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = rule[1..];
            return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return host.Equals(rule, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"(?i)(password|secret|api[_-]?key|connectionstring)\s*[:=]\s*['""][^'""]+['""]", RegexOptions.None)]
    private static partial Regex SecretPattern();
}
