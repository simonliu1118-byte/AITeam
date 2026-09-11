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

public sealed class ProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        string? standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var executable = CliExecutableResolver.Resolve(fileName);
        if (executable is null)
        {
            throw new FileNotFoundException($"找不到 {fileName}。", fileName);
        }

        var start = DateTimeOffset.UtcNow;
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        psi.Environment["NO_COLOR"] = "1";
        psi.Environment["POWERSHELL_TELEMETRY_OPTOUT"] = "1";

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

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

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
}
