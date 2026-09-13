using System.Text.Json;
using System.Text.Json.Serialization;

namespace AITeam.Services;

public enum TaskOutcome
{
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// 一筆歷史任務的摘要。完整執行 log 另外存成檔案，只有使用者點開那一筆時才讀，
/// 清單本身才不會因為 log 越積越多而變慢。
/// </summary>
public sealed class TaskHistoryEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("project")]
    public string ProjectName { get; set; } = "";

    /// <summary>由 AI 產生的短主旨；AI 沒給時退回使用者輸入的開頭。</summary>
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = "";

    [JsonPropertyName("request")]
    public string Request { get; set; } = "";

    // 存成字串而不是數字：之後 enum 順序若調整，舊紀錄才不會整批對應到錯的值。
    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TaskKind Kind { get; set; } = TaskKind.Inquiry;

    [JsonPropertyName("outcome")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TaskOutcome Outcome { get; set; } = TaskOutcome.Completed;

    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("finished_at")]
    public DateTime FinishedAt { get; set; }

    /// <summary>結果摘要（最後的回答或完成訊息），清單就看得到，不必開檔。</summary>
    [JsonPropertyName("result")]
    public string Result { get; set; } = "";

    [JsonIgnore]
    public TimeSpan Duration => FinishedAt > StartedAt ? FinishedAt - StartedAt : TimeSpan.Zero;
}

public sealed class TaskHistoryDocument
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("tasks")]
    public List<TaskHistoryEntry> Tasks { get; set; } = new();
}

public sealed class TaskHistoryService
{
    /// <summary>保留的筆數上限；超過就連同該筆的 log 檔一起刪掉，避免無限長大。</summary>
    internal const int MaxEntries = 200;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _indexPath;
    private readonly string _logRoot;

    public TaskHistoryService(string runtimeRoot)
    {
        var historyRoot = Path.Combine(runtimeRoot, "history");
        _indexPath = Path.Combine(historyRoot, "index.json");
        _logRoot = Path.Combine(historyRoot, "logs");
    }

    /// <summary>只讀摘要索引，不碰任何 log 檔。</summary>
    public IReadOnlyList<TaskHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_indexPath)) return Array.Empty<TaskHistoryEntry>();
            var document = JsonSerializer.Deserialize<TaskHistoryDocument>(File.ReadAllText(_indexPath));
            return document?.Tasks ?? new List<TaskHistoryEntry>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 紀錄壞掉不該讓主功能連帶失效，當成沒有歷史即可。
            return Array.Empty<TaskHistoryEntry>();
        }
    }

    /// <summary>讀某一筆的完整執行 log；點開那一筆時才呼叫。</summary>
    public string LoadLog(string id)
    {
        try
        {
            var path = LogPathFor(id);
            return File.Exists(path) ? File.ReadAllText(path) : "（這筆任務沒有保留完整執行紀錄。）";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "（讀取執行紀錄失敗：" + ex.Message + "）";
        }
    }

    public void Save(TaskHistoryEntry entry, string fullLog)
    {
        Directory.CreateDirectory(_logRoot);

        var tasks = Load().ToList();
        tasks.Insert(0, entry);

        foreach (var dropped in tasks.Skip(MaxEntries))
            TryDeleteLog(dropped.Id);
        if (tasks.Count > MaxEntries) tasks = tasks.Take(MaxEntries).ToList();

        File.WriteAllText(LogPathFor(entry.Id), fullLog);
        File.WriteAllText(
            _indexPath,
            JsonSerializer.Serialize(new TaskHistoryDocument { Tasks = tasks }, WriteOptions));
    }

    private string LogPathFor(string id) => Path.Combine(_logRoot, SafeFileName(id) + ".log");

    private void TryDeleteLog(string id)
    {
        try
        {
            var path = LogPathFor(id);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 刪不掉舊 log 不影響新紀錄寫入，忽略即可。
        }
    }

    internal static string SafeFileName(string id) =>
        new(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
}
