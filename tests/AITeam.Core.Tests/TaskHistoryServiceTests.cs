using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class TaskHistoryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aiteam-history-" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public void NoHistoryYet_LoadsAsEmpty_WithoutThrowing()
    {
        var history = new TaskHistoryService(_root);
        Assert.Empty(history.Load());
    }

    [Fact]
    public void SavedTask_AppearsInTheIndex_AndItsLogIsReadSeparately()
    {
        var history = new TaskHistoryService(_root);
        history.Save(NewEntry("t1", "把清單改成卡片"), "第一行\r\n第二行");

        var entries = history.Load();

        var entry = Assert.Single(entries);
        Assert.Equal("把清單改成卡片", entry.Subject);
        Assert.Equal(TaskOutcome.Completed, entry.Outcome);
        // 摘要在索引裡就看得到，完整 log 要另外讀。
        Assert.Equal("完成了", entry.Result);
        Assert.Contains("第二行", history.LoadLog("t1"));
    }

    [Fact]
    public void NewestTaskComesFirst()
    {
        var history = new TaskHistoryService(_root);
        history.Save(NewEntry("old", "舊任務"), "log");
        history.Save(NewEntry("new", "新任務"), "log");

        Assert.Equal(new[] { "新任務", "舊任務" }, history.Load().Select(x => x.Subject));
    }

    [Fact]
    public void MissingLog_SaysSo_InsteadOfThrowing()
    {
        var history = new TaskHistoryService(_root);
        Assert.Contains("沒有保留完整執行紀錄", history.LoadLog("not-there"));
    }

    [Fact]
    public void CorruptIndex_IsTreatedAsEmpty_SoTheAppStillWorks()
    {
        Directory.CreateDirectory(Path.Combine(_root, "history"));
        File.WriteAllText(Path.Combine(_root, "history", "index.json"), "{ this is not json");

        Assert.Empty(new TaskHistoryService(_root).Load());
    }

    [Fact]
    public void OldEntriesAreTrimmed_SoHistoryCannotGrowForever()
    {
        var history = new TaskHistoryService(_root);
        for (var i = 0; i < TaskHistoryService.MaxEntries + 5; i++)
            history.Save(NewEntry("task-" + i, "任務 " + i), "log " + i);

        var entries = history.Load();

        Assert.Equal(TaskHistoryService.MaxEntries, entries.Count);
        Assert.Equal("任務 " + (TaskHistoryService.MaxEntries + 4), entries[0].Subject);
        // 被擠掉的那幾筆，log 檔也要一起清掉，不能留下孤兒檔案。
        Assert.Contains("沒有保留完整執行紀錄", history.LoadLog("task-0"));
    }

    private static TaskHistoryEntry NewEntry(string id, string subject) => new()
    {
        Id = id,
        ProjectName = "CYAccounting",
        Subject = subject,
        Request = "原始輸入",
        Kind = TaskKind.Change,
        Outcome = TaskOutcome.Completed,
        StartedAt = DateTime.Now.AddMinutes(-3),
        FinishedAt = DateTime.Now,
        Result = "完成了"
    };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
