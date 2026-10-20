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
            var failure = ClassifyFailure(provider, combined, result.Duration, result.ExitCode);
            var log = WriteFullOutput(provider, combined);
            return log is null ? failure : failure with { Detail = $"{failure.Detail}（完整內容：{log}）" };
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
        // 不管分類成哪一種狀態，CLI 原文都要一起帶回去；畫面只顯示分類後的短句，
        // 原文則寫進執行紀錄，分類判斷萬一有漏，使用者至少看得到 CLI 真正說了什麼。
        // CLI 用 JSON 回報時，先把裡面真正的錯誤訊息挑出來，不然紀錄裡只看得到一堆欄位。
        var messages = ExtractCliMessages(text);
        var detail = messages.Count > 0
            ? (exitCode is null ? "" : $"exit {exitCode}｜") + string.Join("｜", messages)
            : Summarize(text, exitCode);

        // 有挑出錯誤訊息時只看訊息本身：原始 JSON 裡的欄位名稱（例如 rate_limit 之類的統計欄位）
        // 會讓「登入過期」被誤判成「超過限額」。
        var judged = messages.Count > 0 ? string.Join("\n", messages) : text;
        var lower = Normalize(judged);

        if (LooksLikeQuota(judged) || ContainsAny(lower, "credit", "try again at", "resets at", "resets on", "resets in"))
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
                "failed to authenticate",
                "oauth",
                "session expired",
                "please run /login",
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
            flattened = flattened[..max] + "（後面省略）";
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

    /// <summary>
    /// 從 CLI 的 JSON 輸出挑出給人看的錯誤訊息：Codex 的 error 事件（裡面常常又包一層 API 回應的 JSON）、
    /// Claude 標了 is_error 的 result。沒有 JSON 或找不到訊息就回空清單。
    /// </summary>
    internal static IReadOnlyList<string> ExtractCliMessages(string text)
    {
        var messages = new List<string>();
        foreach (var raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
            {
                AddPlainError(line, messages);
                continue;
            }
            if (!line.StartsWith('{')) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                CollectMessages(document.RootElement, messages);
            }
            catch (JsonException)
            {
            }
        }
        return messages.Distinct().ToList();
    }

    private static void CollectMessages(JsonElement element, List<string> messages)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        var type = element.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;

        if (type == "error" && element.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
        {
            AddMessage(message.GetString()!, messages);
            return;
        }

        if (element.TryGetProperty("item", out var item)) CollectMessages(item, messages);

        if (element.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True)
        {
            if (element.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(result.GetString()))
                AddMessage(result.GetString()!, messages);
            else if (element.TryGetProperty("subtype", out var subtype) && subtype.ValueKind == JsonValueKind.String)
                messages.Add(subtype.GetString()!);
        }

        if (element.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.String) AddMessage(error.GetString()!, messages);
            else if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var inner) && inner.ValueKind == JsonValueKind.String)
                messages.Add(inner.GetString()!.Trim());
        }
    }

    /// <summary>
    /// 純文字的「error: …」行（Antigravity 就是這樣回報），後面常接著一段給程式看的 JSON，
    /// 只留前面給人看的句子。不是 CLI 參數錯誤的那種 error（交給精簡參數重試判斷）。
    /// </summary>
    private static void AddPlainError(string line, List<string> messages)
    {
        if (LooksLikeUnsupportedArgument(line)) return;
        var text = line["error:".Length..];
        var json = text.IndexOf('{');
        if (json >= 0) text = text[..json];
        text = text.Trim();
        // 去掉接在 JSON 前面的標籤，例如「AGY_ERROR:」。
        var lastSpace = text.LastIndexOf(' ');
        if (text.EndsWith(':') && lastSpace > 0) text = text[..lastSpace].Trim();
        if (text.Length > 0) messages.Add(text);
    }

    /// <summary>訊息本身可能又是一段 JSON（API 的錯誤回應），能拆就再拆一層。</summary>
    private static void AddMessage(string message, List<string> messages)
    {
        var trimmed = message.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var nested = JsonDocument.Parse(trimmed);
                var before = messages.Count;
                CollectMessages(nested.RootElement, messages);
                if (messages.Count > before) return;
            }
            catch (JsonException)
            {
            }
        }
        if (trimmed.Length > 0) messages.Add(trimmed);
    }

    /// <summary>
    /// 健康檢查失敗時，把 CLI 的完整輸出存到 logs 資料夾（每家一個檔，只留最後一次），
    /// 紀錄裡只放整理過的重點，需要時再打開這個檔看全文。寫不進去就算了，不影響檢查結果。
    /// </summary>
    private string? WriteFullOutput(ProviderId provider, string output)
    {
        try
        {
            var directory = Path.Combine(_runtimeRoot, "logs");
            Directory.CreateDirectory(directory);
            var name = $"health-{provider.ToString().ToLowerInvariant()}.log";
            File.WriteAllText(Path.Combine(directory, name), $"[{DateTimeOffset.Now:O}]\r\n{output.Trim()}\r\n");
            return Path.Combine("logs", name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
