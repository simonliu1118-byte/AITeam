using System.Text.Json;
using AITeam.Models;

namespace AITeam.Services;

public static class RuntimeRootResolver
{
    public static string Resolve()
    {
        var env = Environment.GetEnvironmentVariable("AITEAM_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return Path.GetFullPath(env);
        }

        var baseDir = Path.GetFullPath(AppContext.BaseDirectory);
        var trimmed = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);

        if (leaf.Equals("current", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(trimmed);
            if (parent is not null)
            {
                return parent.FullName;
            }
        }

        const string standardRoot = @"D:\AITeam";
        if (Directory.Exists(standardRoot))
        {
            return standardRoot;
        }

        return baseDir;
    }
}

public static class CrashLogger
{
    private static readonly object Sync = new();
    public static string LogPath { get; private set; } = Path.Combine(Path.GetTempPath(), "AITeam-startup.log");

    public static void Initialize(string runtimeRoot)
    {
        try
        {
            var logDir = Path.Combine(runtimeRoot, "logs");
            Directory.CreateDirectory(logDir);
            LogPath = Path.Combine(logDir, "exe-startup-error.log");
        }
        catch
        {
            LogPath = Path.Combine(Path.GetTempPath(), "AITeam-startup-error.log");
        }
    }

    public static void Write(string title, Exception ex)
    {
        try
        {
            lock (Sync)
            {
                File.AppendAllText(
                    LogPath,
                    $"[{DateTimeOffset.Now:O}] {title}\r\n{ex}\r\n\r\n",
                    new System.Text.UTF8Encoding(false));
            }
        }
        catch
        {
            // Never throw from crash logging.
        }
    }
}

public sealed class ProjectRegistryService
{
    private readonly string _runtimeRoot;

    public ProjectRegistryService(string runtimeRoot)
    {
        _runtimeRoot = runtimeRoot;
    }

    public string? RegistryPath { get; private set; }

    public IReadOnlyList<ProjectEntry> Load()
    {
        var candidates = new[]
        {
            Path.Combine(_runtimeRoot, "data", "projects.json"),
            Path.Combine(_runtimeRoot, "config", "projects.json")
        };

        var path = candidates.FirstOrDefault(File.Exists);
        RegistryPath = path;

        if (path is null)
        {
            return Array.Empty<ProjectEntry>();
        }

        var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
        var document = JsonSerializer.Deserialize<ProjectRegistryDocument>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });

        return document?.Projects
            .Where(p => p.Active is not false)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? Array.Empty<ProjectEntry>();
    }
}
