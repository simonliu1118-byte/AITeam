using System.Text.Json;
using AITeam.Models;

namespace AITeam.Services;

public sealed class ProviderHealthService
{
    private readonly string _runtimeRoot;
    private readonly ProcessRunner _runner;

    public ProviderHealthService(string runtimeRoot, ProcessRunner runner)
    {
        _runtimeRoot = runtimeRoot;
        _runner = runner;
    }

    public Task<ProviderHealth> ProbeAsync(ProviderId provider, CancellationToken cancellationToken) =>
        provider switch
        {
            ProviderId.Codex => ProbeCodexAsync(cancellationToken),
            ProviderId.Claude => ProbeClaudeAsync(cancellationToken),
            ProviderId.Antigravity => ProbeAntigravityAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };

    private async Task<ProviderHealth> ProbeCodexAsync(CancellationToken cancellationToken)
    {
        const string prompt = "Return exactly AITEAM_HEALTH_OK. Do not inspect files and do not use tools.";

        var args = new[]
        {
            "--sandbox", "read-only",
            "--ask-for-approval", "never",
            "-c", "model_reasoning_effort=\"low\"",
            "exec",
            "--skip-git-repo-check",
            "--json",
            "-"
        };

        return await RunAndClassifyAsync(
            ProviderId.Codex,
            "codex",
            args,
            prompt,
            TimeSpan.FromSeconds(60),
            result =>
            {
                if (result.ExitCode != 0)
                {
                    return false;
                }

                if (result.StandardOutput.Contains("\"type\":\"error\"", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return result.StandardOutput.Contains("\"type\":\"turn.completed\"", StringComparison.OrdinalIgnoreCase)
                    || result.StandardOutput.Contains("AITEAM_HEALTH_OK", StringComparison.OrdinalIgnoreCase);
            },
            cancellationToken);
    }

    private async Task<ProviderHealth> ProbeClaudeAsync(CancellationToken cancellationToken)
    {
        const string prompt = "Return exactly AITEAM_HEALTH_OK. Do not inspect files and do not use tools.";

        var args = new[]
        {
            "-p", prompt,
            "--output-format", "json",
            "--max-turns", "1",
            "--model", "sonnet",
            "--permission-mode", "bypassPermissions",
            "--disable-slash-commands",
            "--no-session-persistence"
        };

        return await RunAndClassifyAsync(
            ProviderId.Claude,
            "claude",
            args,
            null,
            TimeSpan.FromSeconds(60),
            result => result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput),
            cancellationToken);
    }

    private async Task<ProviderHealth> ProbeAntigravityAsync(CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            @event = "user",
            message = new
            {
                content = "Return exactly AITEAM_HEALTH_OK. Do not inspect files and do not use tools."
            }
        }) + Environment.NewLine;

        var args = new[]
        {
            "--dangerously-skip-permissions",
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--print-timeout", "2m"
        };

        return await RunAndClassifyAsync(
            ProviderId.Antigravity,
            "agy",
            args,
            payload,
            TimeSpan.FromSeconds(90),
            result =>
            {
                if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    return false;
                }

                return result.StandardOutput.Contains("\"status\":\"SUCCESS\"", StringComparison.OrdinalIgnoreCase)
                    || result.StandardOutput.Contains("\"event\":\"result\"", StringComparison.OrdinalIgnoreCase)
                    || result.StandardOutput.Contains("AITEAM_HEALTH_OK", StringComparison.OrdinalIgnoreCase);
            },
            cancellationToken);
    }

    private async Task<ProviderHealth> RunAndClassifyAsync(
        ProviderId provider,
        string executable,
        IReadOnlyList<string> arguments,
        string? standardInput,
        TimeSpan timeout,
        Func<ProcessRunResult, bool> successPredicate,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _runner.RunAsync(
                executable,
                arguments,
                _runtimeRoot,
                standardInput,
                timeout,
                cancellationToken);

            if (successPredicate(result))
            {
                return new ProviderHealth(provider, ProviderHealthState.Online, "上線", result.Duration);
            }

            var combined = $"{result.StandardError}\n{result.StandardOutput}";
            return ClassifyFailure(provider, combined, result.Duration);
        }
        catch (FileNotFoundException ex)
        {
            return new ProviderHealth(provider, ProviderHealthState.Missing, "CLI 未安裝", TimeSpan.Zero);
        }
        catch (TimeoutException ex)
        {
            return new ProviderHealth(provider, ProviderHealthState.TemporaryError, ex.Message, timeout);
        }
        catch (OperationCanceledException)
        {
            return new ProviderHealth(provider, ProviderHealthState.TemporaryError, "檢查已取消", TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            return new ProviderHealth(provider, ProviderHealthState.Error, ex.Message, TimeSpan.Zero);
        }
    }

    private static ProviderHealth ClassifyFailure(
        ProviderId provider,
        string text,
        TimeSpan duration)
    {
        var lower = text.ToLowerInvariant();

        if (ContainsAny(lower,
                "usage limit",
                "quota",
                "rate limit",
                "resource exhausted",
                "too many requests",
                "credit",
                "limit reached",
                "try again at"))
        {
            return new ProviderHealth(provider, ProviderHealthState.Quota, "超過限額", duration);
        }

        if (ContainsAny(lower,
                "not logged in",
                "login required",
                "sign in",
                "unauthorized",
                "authentication",
                "auth required",
                "credential"))
        {
            return new ProviderHealth(provider, ProviderHealthState.AuthenticationRequired, "需要重新登入", duration);
        }

        if (ContainsAny(lower,
                "timeout",
                "timed out",
                "network",
                "connection reset",
                "connection refused",
                "temporarily unavailable",
                "service unavailable",
                "bad gateway",
                "gateway timeout"))
        {
            return new ProviderHealth(provider, ProviderHealthState.TemporaryError, "暫時異常", duration);
        }

        var firstLine = text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim();

        return new ProviderHealth(
            provider,
            ProviderHealthState.Error,
            string.IsNullOrWhiteSpace(firstLine) ? "異常" : firstLine,
            duration);
    }

    private static bool ContainsAny(string text, params string[] candidates) =>
        candidates.Any(text.Contains);
}
