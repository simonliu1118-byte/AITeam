using System.Text.Json;
using AITeam.Models;

namespace AITeam.Services;

public sealed class ProviderHealthService
{
    private readonly string _runtimeRoot;
    private readonly IProcessRunner _runner;
    private readonly AgentsConfig _agents;

    public ProviderHealthService(string runtimeRoot, IProcessRunner runner)
    {
        _runtimeRoot = runtimeRoot;
        _runner = runner;
        _agents = new RuntimeConfigService(runtimeRoot).LoadAgentsConfig();
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
            _agents.CodexCommand,
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
            _agents.ClaudeCommand,
            args,
            null,
            TimeSpan.FromSeconds(60),
            // Claude CLI 超過限額時仍可能以 exit code 0 回傳，只在 JSON 裡標 is_error，
            // 只看 exit code 會把「超過限額」誤判成上線，因此一併檢查這個旗標。
            result => result.ExitCode == 0
                      && !string.IsNullOrWhiteSpace(result.StandardOutput)
                      && !IsJsonErrorResult(result.StandardOutput),
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
            _agents.AntigravityCommand,
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

    internal static ProviderHealth ClassifyFailure(
        ProviderId provider,
        string text,
        TimeSpan duration)
    {
        // CLI 的訊息有兩種寫法：給人看的句子（"usage limit reached"）與 API 機器代碼
        // （"rate_limit_error"、"resource_exhausted"）。先把底線與連字號正規化成空白，
        // 同一組關鍵字才能同時比對到兩種寫法，不會因為寫法不同就誤判成一般錯誤。
        var lower = Normalize(text);

        if (ContainsAny(lower,
                "usage limit",
                "quota",
                "rate limit",
                "resource exhausted",
                "too many requests",
                "credit",
                "limit reached",
                "limit exceeded",
                "exceeded your",
                "reached your limit",
                "try again at",
                "resets at",
                "upgrade your plan",
                "429 too many",
                "error 429",
                "status 429",
                "http 429"))
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

    internal static bool IsJsonErrorResult(string output)
    {
        var compact = output.Replace(" ", "").Replace("\r", "").Replace("\n", "");
        return compact.Contains("\"is_error\":true", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string text) =>
        text.ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');

    private static bool ContainsAny(string text, params string[] candidates) =>
        candidates.Any(text.Contains);
}
