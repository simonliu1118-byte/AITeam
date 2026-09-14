using System.Text.Json;
using AITeam.Models;

namespace AITeam.Services;

/// <summary>
/// 用唯讀方式問一家 AI 一個問題。查詢任務與 AI 四方會議都需要這件事，
/// 三家 CLI 的參數與輸出解析各不相同，集中在這裡，不要每個功能各抄一份。
/// （修改任務另外有可寫入的版本，暫時留在 ChangeTaskService。）
/// </summary>
public sealed class ProviderClient
{
    private readonly string _runtimeRoot;
    private readonly IProcessRunner _runner;
    private readonly AgentsConfig _agents;

    public ProviderClient(string runtimeRoot, IProcessRunner runner, AgentsConfig agents)
    {
        _runtimeRoot = runtimeRoot;
        _runner = runner;
        _agents = agents;
    }

    public Task<string> AskAsync(
        ProviderId provider,
        string workingDirectory,
        string prompt,
        TimeSpan timeout,
        Action<string>? onActivity,
        CancellationToken cancellationToken)
    {
        var sink = MakeSink(provider, onActivity);
        return provider switch
        {
            ProviderId.Codex => AskCodexAsync(workingDirectory, prompt, timeout, sink, cancellationToken),
            ProviderId.Claude => AskClaudeAsync(workingDirectory, prompt, timeout, sink, cancellationToken),
            ProviderId.Antigravity => AskAntigravityAsync(workingDirectory, prompt, timeout, sink, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    private static Action<string>? MakeSink(ProviderId provider, Action<string>? onActivity)
    {
        if (onActivity is null) return null;
        return line =>
        {
            var described = ProviderActivity.Describe(provider, line);
            if (described is not null) onActivity($"{provider.ToFriendlyName()}：{described}");
        };
    }

    private async Task<string> AskCodexAsync(
        string workingDirectory, string prompt, TimeSpan timeout, Action<string>? sink, CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(_runtimeRoot, "temp");
        Directory.CreateDirectory(tempDir);
        var lastMessage = Path.Combine(tempDir, "codex-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var result = await _runner.RunAsync(
                _agents.CodexCommand,
                new[]
                {
                    "--sandbox", "read-only",
                    "--ask-for-approval", "never",
                    "-c", "model_reasoning_effort=\"medium\"",
                    "exec",
                    "--skip-git-repo-check",
                    "--output-last-message", lastMessage,
                    "-"
                },
                workingDirectory,
                prompt,
                timeout,
                cancellationToken,
                sink);
            if (result.ExitCode != 0) throw new InvalidOperationException(DescribeFailure(result));

            return File.Exists(lastMessage)
                ? await File.ReadAllTextAsync(lastMessage, cancellationToken)
                : result.StandardOutput;
        }
        finally
        {
            try { if (File.Exists(lastMessage)) File.Delete(lastMessage); } catch { }
        }
    }

    private async Task<string> AskClaudeAsync(
        string workingDirectory, string prompt, TimeSpan timeout, Action<string>? sink, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            _agents.ClaudeCommand,
            new[]
            {
                "-p", prompt,
                "--output-format", "text",
                "--max-turns", "20",
                "--model", "sonnet",
                "--permission-mode", "plan",
                "--no-session-persistence"
            },
            workingDirectory,
            null,
            timeout,
            cancellationToken,
            sink);
        if (result.ExitCode != 0) throw new InvalidOperationException(DescribeFailure(result));
        return result.StandardOutput;
    }

    private async Task<string> AskAntigravityAsync(
        string workingDirectory, string prompt, TimeSpan timeout, Action<string>? sink, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            @event = "user",
            message = new { content = prompt }
        }) + Environment.NewLine;

        // CLI 自己的逾時要比外層短一點，讓它有機會好好收尾而不是被硬砍。
        var printTimeout = Math.Max(1, (int)timeout.TotalMinutes - 1);
        var result = await _runner.RunAsync(
            _agents.AntigravityCommand,
            new[]
            {
                "--dangerously-skip-permissions",
                "--input-format", "stream-json",
                "--output-format", "stream-json",
                "--print-timeout", $"{printTimeout}m"
            },
            workingDirectory,
            payload,
            timeout,
            cancellationToken,
            sink);
        if (result.ExitCode != 0) throw new InvalidOperationException(DescribeFailure(result));

        var extracted = AntigravityStream.ExtractAnswer(result.StandardOutput);
        return string.IsNullOrWhiteSpace(extracted)
            ? AntigravityStream.DescribeUnparsableOutput(result.StandardOutput)
            : extracted;
    }

    /// <summary>失敗訊息要帶上結束代碼，不然「失敗了」三個字對查問題毫無幫助。</summary>
    internal static string DescribeFailure(ProcessRunResult result)
    {
        var text = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        var line = text
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim();
        return string.IsNullOrWhiteSpace(line)
            ? $"CLI 以結束代碼 {result.ExitCode} 結束，而且沒有輸出任何訊息。"
            : $"exit {result.ExitCode}｜{line}";
    }
}
