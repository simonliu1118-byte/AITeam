using System.Text;
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
                    new UTF8Encoding(false));
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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    public ProjectRegistryService(string runtimeRoot)
    {
        _runtimeRoot = runtimeRoot;
    }

    public string? RegistryPath { get; private set; }

    public IReadOnlyList<ProjectEntry> Load()
    {
        var document = LoadDocument();
        return document.Projects
            .Where(p => p.Active is not false)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ProjectRegistryDocument LoadDocument()
    {
        var path = ResolveRegistryPath();
        RegistryPath = path;

        if (!File.Exists(path))
        {
            return new ProjectRegistryDocument();
        }

        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<ProjectRegistryDocument>(json, JsonOptions)
            ?? new ProjectRegistryDocument();
    }

    public void Save(ProjectRegistryDocument document)
    {
        var path = ResolveRegistryPath();
        RegistryPath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine;
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    public void Upsert(ProjectEntry entry, string? originalName = null)
    {
        var document = LoadDocument();
        var key = string.IsNullOrWhiteSpace(originalName) ? entry.Name : originalName;
        var index = document.Projects.FindIndex(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            document.Projects[index] = entry;
        }
        else
        {
            document.Projects.Add(entry);
        }
        Save(document);
    }

    public void Remove(string name)
    {
        var document = LoadDocument();
        document.Projects.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        Save(document);
    }

    private string ResolveRegistryPath()
    {
        var data = Path.Combine(_runtimeRoot, "data", "projects.json");
        var legacy = Path.Combine(_runtimeRoot, "config", "projects.json");
        if (File.Exists(data)) return data;
        if (File.Exists(legacy)) return legacy;
        return data;
    }
}

public sealed class KnownRepositoryService
{
    private readonly string _runtimeRoot;

    public KnownRepositoryService(string runtimeRoot)
    {
        _runtimeRoot = runtimeRoot;
    }

    public IReadOnlyList<string> Load(IEnumerable<ProjectEntry> projects)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            if (!string.IsNullOrWhiteSpace(project.GitHubRepo)) names.Add(project.GitHubRepo.Trim());
        }

        foreach (var path in new[]
        {
            Path.Combine(_runtimeRoot, "data", "repositories.json"),
            Path.Combine(_runtimeRoot, "config", "repositories.json")
        })
        {
            if (!File.Exists(path)) continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (document.RootElement.TryGetProperty("repositories", out var repos) && repos.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in repos.EnumerateArray())
                    {
                        var name = item.GetString();
                        if (!string.IsNullOrWhiteSpace(name)) names.Add(name.Trim());
                    }
                }
            }
            catch
            {
                // Ignore malformed optional repository history.
            }
        }

        return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Remember(string githubRepo, IEnumerable<ProjectEntry> projects)
    {
        var repo = githubRepo.Trim();
        if (repo.Length == 0) return;

        var names = new HashSet<string>(Load(projects), StringComparer.OrdinalIgnoreCase) { repo };
        var path = File.Exists(Path.Combine(_runtimeRoot, "data", "repositories.json"))
            ? Path.Combine(_runtimeRoot, "data", "repositories.json")
            : Path.Combine(_runtimeRoot, "config", "repositories.json");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = new { schema_version = 1, repositories = names.OrderBy(x => x).ToArray() };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
    }
}
