using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AITeam.Models;

namespace AITeam.Services;

public static class CliExecutableResolver
{
    public static string? Resolve(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        if (Path.IsPathRooted(command) && File.Exists(command))
        {
            return Path.GetFullPath(command);
        }

        foreach (var candidate in GetKnownCandidates(command))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var raw in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var directory = raw.Trim().Trim('"');
                if (directory.Length == 0)
                {
                    continue;
                }

                var exe = Path.Combine(directory, command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? command
                    : command + ".exe");
                if (File.Exists(exe))
                {
                    return Path.GetFullPath(exe);
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    private static IEnumerable<string> GetKnownCandidates(string command)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (command.Equals("codex", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(local, "Programs", "OpenAI", "Codex", "bin", "codex.exe");
        }
        else if (command.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(user, ".local", "bin", "claude.exe");
        }
        else if (command.Equals("agy", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(local, "agy", "bin", "agy.exe");
        }
        else if (command.Equals("git", StringComparison.OrdinalIgnoreCase))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(programFiles, "Git", "cmd", "git.exe");
            yield return Path.Combine(programFiles, "Git", "bin", "git.exe");
        }
    }
}

public interface IProcessRunner
{
    /// <param name="onOutputLine">
    /// CLI 每吐出一行就會被呼叫一次（stdout 與 stderr 都會）。傳 null 就是以前的行為：
    /// 跑完才一次拿到全部輸出。有了這個回呼，實作階段才不會是一個十幾分鐘、
    /// 畫面上完全沒有動靜的黑洞。可能來自背景執行緒。
    /// </param>
    Task<ProcessRunResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        string? standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action<string>? onOutputLine = null);
}

public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>
    /// 讓 CLI 沿用本程式已經配置好的（隱藏的）主控台，而不是自己開一個新的。
    /// 預設 false＝維持原本一定能動的作法。這是整個行程共用的狀態（我們自己有沒有隱藏主控台
    /// 是行程層級的事實），所以放 static；使用者在設定裡切換時即時生效，不用重開程式。
    /// 沒有隱藏主控台卻把這個打開，反而會讓每個 CLI 自己開一個看得見的視窗，
    /// 所以只有在確認主控台真的存在且已隱藏之後才可以設成 true。
    /// </summary>
    public static bool InheritHiddenConsole { get; set; }

    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        string? standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action<string>? onOutputLine = null)
    {
        var executable = CliExecutableResolver.Resolve(fileName);
        if (executable is null)
        {
            throw new FileNotFoundException($"找不到 {fileName}。", fileName);
        }

        var start = DateTimeOffset.UtcNow;
        var psi = CreateStartInfo(executable, arguments, workingDirectory, standardInput, InheritHiddenConsole);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Unable to start {fileName}.");
            }
        }
        catch (Win32Exception ex)
        {
            throw new FileNotFoundException($"找不到 {fileName}。", executable, ex);
        }

        // 逐行讀而不是 ReadToEnd：一邊累積完整輸出，一邊把每一行往外送給畫面。
        var stdoutTask = ReadAllAsync(process.StandardOutput, onOutputLine);
        var stderrTask = ReadAllAsync(process.StandardError, onOutputLine);

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort kill.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"{fileName} timed out after {timeout.TotalSeconds:0} seconds.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var duration = DateTimeOffset.UtcNow - start;

        return new ProcessRunResult(process.ExitCode, stdout, stderr, duration);
    }

    /// <summary>
    /// 建立行程啟動設定。抽出來是為了測得到——尤其是 stdin 的編碼。
    /// </summary>
    internal static ProcessStartInfo CreateStartInfo(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory,
        string? standardInput,
        bool inheritHiddenConsole = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            // CreateNoWindow 會讓 CLI 拿到一個屬於它自己的新主控台。它自己不會冒出視窗，
            // 但它再去叫起來的孫程序（例如 Codex 在 Windows 上用 PowerShell 跑工具）
            // 有機會另外開一個看得見的視窗，那就是畫面上一閃而過的黑框。
            // 關掉 CreateNoWindow 時，CLI 會沿用我們自己那個已經隱藏起來的主控台，
            // 孫程序也跟著繼承，理論上就不會再閃。只有在主控台確實已經隱藏時才這樣做。
            CreateNoWindow = !inheritHiddenConsole,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            // stdin 的編碼一定要跟著指定成 UTF-8（不含 BOM）。沒有指定時 .NET 會用系統的
            // ANSI 代碼頁（在繁體中文 Windows 上是 CP950），提示裡只要有中文，CLI 收到的
            // 就是不合法的 UTF-8，會直接以
            // 「input is not valid UTF-8 (invalid byte at offset N)」失敗。
            // 只有從 stdin 收提示的 CLI（Codex、Antigravity）會踩到；Claude 用參數傳提示，
            // 所以同一份中文提示它收得到、另外兩家收不到。
            StandardInputEncoding = standardInput is null ? null : new UTF8Encoding(false)
        };

        foreach (var argument in arguments) psi.ArgumentList.Add(argument);

        psi.Environment["NO_COLOR"] = "1";
        psi.Environment["POWERSHELL_TELEMETRY_OPTOUT"] = "1";
        return psi;
    }

    private static async Task<string> ReadAllAsync(StreamReader reader, Action<string>? onOutputLine)
    {
        var all = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            all.Append(line).Append('\n');
            if (onOutputLine is null) continue;

            try
            {
                onOutputLine(line);
            }
            catch
            {
                // 回呼（通常是更新畫面）出問題不該讓整個工作失敗。
            }
        }

        return all.ToString();
    }
}
