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
            cancellationToken,
            fallbackArguments: new[] { "exec", "--skip-git-repo-check", "--json", "-" });
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
            cancellationToken,
            fallbackArguments: new[] { "-p", prompt, "--output-format", "json" });
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
            cancellationToken,
            fallbackArguments: new[]
            {
                "--dangerously-skip-permissions",
                "--input-format", "stream-json",
                "--output-format", "stream-json"
            });
    }

    private async Task<ProviderHealth> RunAndClassifyAsync(
        ProviderId provider,
        string executable,
        IReadOnlyList<string> arguments,
        string? standardInput,
        TimeSpan timeout,
        Func<ProcessRunResult, bool> successPredicate,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? fallbackArguments = null)
    {
        var health = await RunOnceAsync(
            provider, executable, arguments, standardInput, timeout, successPredicate, cancellationToken);

        // CLI 改版把某個參數拿掉時，健康檢查會直接被 CLI 擋下來（「unknown option」），
        // 於是不管額度夠不夠、有沒有登入，畫面都只會顯示「錯誤」。這種情況改用最精簡的
        // 參數再試一次，至少能問出這家 AI 真正的狀態，而不是卡在參數問題上。
        if (fallbackArguments is { Count: > 0 }
            && health.State == ProviderHealthState.Error
            && LooksLikeUnsupportedArgument(health.Detail))
        {
            var retried = await RunOnceAsync(
                provider, executable, fallbackArguments, standardInput, timeout, successPredicate, cancellationToken);
            return retried with { Detail = Summarize($"{health.Detail}｜改用精簡參數重試：{retried.Detail}") };
        }

        return health;
    }

    private async Task<ProviderHealth> RunOnceAsync(
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

            // 三家 CLI 都可能在「看起來成功」的輸出裡夾帶限額訊息（正常結束、也有完成事件），
            // 只看各自的成功訊號會把超過限額誤判成上線，接著把工作派給它然後失敗。
            // 因此不管哪一家，先排除帶有限額訊息的輸出。
            var quotaHit = LooksLikeQuota(result.StandardOutput) || LooksLikeQuota(result.StandardError);
            if (!quotaHit && successPredicate(result))
            {
                return new ProviderHealth(provider, ProviderHealthState.Online, "上線", result.Duration);
            }

            var combined = $"{result.StandardError}\n{result.StandardOutput}";
            return ClassifyFailure(provider, combined, result.Duration, result.ExitCode);
        }
        catch (FileNotFoundException ex)
        {
            return new ProviderHealth(provider, ProviderHealthState.Missing, "CLI 未安裝", TimeSpan.Zero, Summarize(ex.Message));
        }
        catch (TimeoutException ex)
        {
            return new ProviderHealth(provider, ProviderHealthState.TemporaryError, ex.Message, timeout, Summarize(ex.Message));
        }
        catch (OperationCanceledException)
        {
            return new ProviderHealth(provider, ProviderHealthState.TemporaryError, "檢查已取消", TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            return new ProviderHealth(provider, ProviderHealthState.Error, ex.Message, TimeSpan.Zero, Summarize(ex.Message));
        }
    }

    internal static ProviderHealth ClassifyFailure(
        ProviderId provider,
        string text,
        TimeSpan duration,
        int? exitCode = null)
    {
        var lower = Normalize(text);
        // 不管分類成哪一種狀態，CLI 原文都要一起帶回去；畫面只顯示分類後的短句，
        // 原文則寫進執行紀錄，分類判斷萬一有漏，使用者至少看得到 CLI 真正說了什麼。
        var detail = Summarize(text, exitCode);

        if (LooksLikeQuota(text) || ContainsAny(lower, "credit", "try again at", "resets at", "resets on"))
        {
            return new ProviderHealth(provider, ProviderHealthState.Quota, "超過限額", duration, detail);
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
            return new ProviderHealth(provider, ProviderHealthState.AuthenticationRequired, "需要重新登入", duration, detail);
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
            return new ProviderHealth(provider, ProviderHealthState.TemporaryError, "暫時異常", duration, detail);
        }

        var firstLine = text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim();

        return new ProviderHealth(
            provider,
            ProviderHealthState.Error,
            string.IsNullOrWhiteSpace(firstLine) ? "異常" : firstLine,
            duration,
            detail);
    }

    /// <summary>
    /// CLI 因為不認得某個參數而根本沒開始工作。這種失敗跟額度、登入都無關，
    /// 訊息長得像一般錯誤，所以要獨立判斷出來、換精簡參數重試。
    /// </summary>
    internal static bool LooksLikeUnsupportedArgument(string text) => ContainsAny(
        Normalize(text),
        "unknown option",
        "unknown argument",
        "unknown flag",
        "unrecognized option",
        "unrecognised option",
        "unexpected argument",
        "invalid option",
        "error: unknown",
        "help for more information",
        "usage: ");

    /// <summary>
    /// 把 CLI 原文壓成一行、去掉空白行並截斷，方便直接寫進執行紀錄。
    /// </summary>
    internal static string Summarize(string text, int? exitCode = null, int max = 400)
    {
        var flattened = string.Join(
            " ",
            text
                .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));

        if (flattened.Length > max)
        {
            flattened = flattened[..max] + "…";
        }

        if (flattened.Length == 0)
        {
            flattened = "（CLI 沒有輸出任何訊息）";
        }

        return exitCode is null ? flattened : $"exit {exitCode}｜{flattened}";
    }

    /// <summary>
    /// 限額訊息的判斷。CLI 有兩種寫法：給人看的句子（"usage limit reached"）與 API 機器代碼
    /// （"rate_limit_error"、"resource_exhausted"）。比對前先把底線與連字號正規化成空白，
    /// 同一組關鍵字才能同時吃到兩種寫法。
    /// </summary>
    internal static bool LooksLikeQuota(string text) => ContainsAny(
        Normalize(text),
        "usage limit",
        "session limit",
        "weekly limit",
        "usage credit",
        "quota",
        "rate limit",
        "resource exhausted",
        "too many requests",
        "limit reached",
        "limit exceeded",
        "exceeded your",
        "reached your limit",
        "hit your limit",
        "credits to keep working",
        "upgrade your plan",
        "out of credit",
        "insufficient credit",
        "429 too many",
        "error 429",
        "status 429",
        "http 429");

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
