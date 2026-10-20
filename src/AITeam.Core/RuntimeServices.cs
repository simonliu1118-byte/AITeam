using System.Text;
using System.Text.Json;
using AITeam.Models;

namespace AITeam.Services;

/// <summary>
/// AITeam 的「家」：專案清單、從 GitHub 抓下來的 Repo、工作區、歷史紀錄都放在這裡。
///
/// 可攜：從 GitHub Release 下載解壓到任何資料夾，那個資料夾就是家，整個資料夾搬到別台電腦
/// 照樣能用。以前只要電腦上有 D:\AITeam 就一定用它，等於綁死在 D 槽。
/// </summary>
public static class RuntimeRootResolver
{
    public const string LegacyRoot = @"D:\AITeam";

    public static string Resolve() => Resolve(
        Environment.GetEnvironmentVariable("AITEAM_ROOT"),
        Path.GetFullPath(AppContext.BaseDirectory),
        Directory.Exists);

    /// <summary>
    /// 順序：
    /// 1. 環境變數 AITEAM_ROOT（測試或特殊安排用）。
    /// 2. exe 放在名為 current 的資料夾裡：家是上一層（舊的安裝方式）。
    /// 3. exe 旁邊已經有 data 資料夾：這個資料夾就是家（可攜）。
    /// 4. 這台電腦有舊的 D:\AITeam：沿用它，既有的專案和紀錄才不會「不見」。
    ///    想讓它變成可攜的，把新的 AITeam.exe 放進 D:\AITeam 就好。
    /// 5. 其他情況：exe 所在的資料夾就是家，第一次啟動時會在這裡建 data。
    /// </summary>
    internal static string Resolve(string? environmentRoot, string baseDirectory, Func<string, bool> directoryExists)
    {
        if (!string.IsNullOrWhiteSpace(environmentRoot)) return Path.GetFullPath(environmentRoot);

        var baseDir = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.GetFileName(baseDir).Equals("current", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(baseDir);
            if (parent is not null) return parent.FullName;
        }

        if (directoryExists(Path.Combine(baseDir, "data"))) return baseDir;
        if (directoryExists(LegacyRoot)) return LegacyRoot;
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
                File.AppendAllText(LogPath,
                    $"[{DateTimeOffset.Now:O}] {title}\r\n{ex}\r\n\r\n",
                    new UTF8Encoding(false));
            }
        }
        catch { }
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

    public ProjectRegistryService(string runtimeRoot) => _runtimeRoot = runtimeRoot;

    public string? RegistryPath { get; private set; }

    /// <summary>
    /// 專案順序就是檔案裡的順序，不再自動照名稱排序——使用者可以在專案管理直接拖曳調整，
    /// 主畫面的下拉選單也會跟著同一個順序。
    /// </summary>
    public IReadOnlyList<ProjectEntry> Load()
    {
        return LoadDocument().Projects
            .Where(p => p.Active is not false)
            .ToList();
    }

    public ProjectRegistryDocument LoadDocument()
    {
        var path = ResolveRegistryPath();
        RegistryPath = path;
        if (!File.Exists(path)) return new ProjectRegistryDocument();

        var json = File.ReadAllText(path, Encoding.UTF8);
        var document = JsonSerializer.Deserialize<ProjectRegistryDocument>(json, JsonOptions)
                       ?? new ProjectRegistryDocument();
        foreach (var project in document.Projects)
        {
            project.RepoPath = ToAbsolute(_runtimeRoot, project.RepoPath, Directory.Exists);
            project.ProjectPath = ToAbsolute(_runtimeRoot, project.ProjectPath, Directory.Exists);
        }
        return document;
    }

    public void Save(ProjectRegistryDocument document)
    {
        var path = ResolveRegistryPath();
        RegistryPath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // 存檔時把家裡的路徑改寫成相對位置，整個資料夾搬家、換磁碟代號都不會對不上。
        // 先複製一份再改，呼叫端手上的物件維持完整路徑。
        var copy = JsonSerializer.Deserialize<ProjectRegistryDocument>(
            JsonSerializer.Serialize(document, JsonOptions), JsonOptions) ?? new ProjectRegistryDocument();
        foreach (var project in copy.Projects)
        {
            project.RepoPath = ToRelative(_runtimeRoot, project.RepoPath);
            project.ProjectPath = ToRelative(_runtimeRoot, project.ProjectPath);
        }

        var json = JsonSerializer.Serialize(copy, JsonOptions) + Environment.NewLine;
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    /// <summary>家裡面的路徑存成相對位置；家外面的（使用者自己放在別處的 Repo）維持原樣。</summary>
    internal static string ToRelative(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return path ?? "";
        var home = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? full[home.Length..] : path;
    }

    /// <summary>
    /// 讀檔時還原成完整路徑。舊版存的是完整路徑：如果那個位置已經不在了（資料夾搬過家），
    /// 但在現在的家裡找得到同一個 repos\… 位置，就改用現在的。
    /// </summary>
    internal static string ToAbsolute(string root, string path, Func<string, bool> directoryExists)
    {
        if (string.IsNullOrWhiteSpace(path)) return path ?? "";
        if (!Path.IsPathRooted(path)) return Path.GetFullPath(Path.Combine(root, path));
        if (directoryExists(path)) return path;

        var normalized = path.Replace('/', '\\');
        var at = normalized.IndexOf("\\repos\\", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return path;

        var relative = normalized[(at + 1)..].Replace('\\', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(root, relative));
        return directoryExists(candidate) ? candidate : path;
    }

    public void Add(ProjectEntry entry)
    {
        var document = LoadDocument();
        if (document.Projects.Any(p => p.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"專案名稱「{entry.Name}」已存在。請使用其他名稱，或從左側清單選取原專案後編輯。");

        document.Projects.Add(entry);
        Save(document);
    }

    public void Update(string originalName, ProjectEntry entry)
    {
        var document = LoadDocument();
        var index = document.Projects.FindIndex(p => p.Name.Equals(originalName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new InvalidOperationException($"找不到要更新的專案「{originalName}」。");

        var conflict = document.Projects
            .Where((p, i) => i != index)
            .Any(p => p.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
        if (conflict) throw new InvalidOperationException($"專案名稱「{entry.Name}」已被其他專案使用。");

        document.Projects[index] = entry;
        Save(document);
    }

    /// <summary>
    /// 依照傳入的名稱順序重新排列登錄檔。清單裡沒提到的專案（例如停用中的）會保留在最後面，
    /// 不會因為排序而不見。
    /// </summary>
    public void Reorder(IReadOnlyList<string> namesInOrder)
    {
        var document = LoadDocument();
        var remaining = document.Projects.ToList();
        var ordered = new List<ProjectEntry>(remaining.Count);

        foreach (var name in namesInOrder)
        {
            var index = remaining.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index < 0) continue;
            ordered.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        ordered.AddRange(remaining);
        document.Projects = ordered;
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
    public KnownRepositoryService(string runtimeRoot) => _runtimeRoot = runtimeRoot;

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
            catch { }
        }

        return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Remember(string githubRepo, IEnumerable<ProjectEntry> projects)
    {
        var repo = githubRepo.Trim();
        if (repo.Length == 0) return;

        var names = new HashSet<string>(Load(projects), StringComparer.OrdinalIgnoreCase) { repo };
        var dataPath = Path.Combine(_runtimeRoot, "data", "repositories.json");
        var legacyPath = Path.Combine(_runtimeRoot, "config", "repositories.json");
        var path = File.Exists(dataPath) ? dataPath : legacyPath;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = new { schema_version = 1, repositories = names.OrderBy(x => x).ToArray() };
        File.WriteAllText(path,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
            new UTF8Encoding(false));
    }
}
