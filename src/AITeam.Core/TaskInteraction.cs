using AITeam.Models;

namespace AITeam.Services;

/// <summary>
/// 任務進行中使用者補充的話。定案之後使用者就插不上嘴，是這個機制要解決的問題：
/// 補充會排在這裡，下一次派工前併進提示裡；標記「立刻套用」的話，
/// 會直接中斷目前這一步、帶著補充重來。
/// </summary>
public sealed class TaskNoteBoard
{
    private readonly object _sync = new();
    private readonly List<string> _pending = new();
    private CancellationTokenSource? _currentStep;

    /// <param name="interruptNow">true 代表不等這一步跑完，立刻中斷並帶著補充重來。</param>
    public void Add(string note, bool interruptNow)
    {
        if (string.IsNullOrWhiteSpace(note)) return;

        CancellationTokenSource? step;
        lock (_sync)
        {
            _pending.Add(note.Trim());
            step = interruptNow ? _currentStep : null;
        }

        // 在鎖外面取消：取消會同步跑 callback，拿著鎖呼叫外面的程式碼容易卡死。
        try
        {
            step?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 那一步剛好結束了，補充會在下一步生效。
        }
    }

    public bool HasPending
    {
        get { lock (_sync) return _pending.Count > 0; }
    }

    /// <summary>取走目前排隊中的補充；取走後就清空。</summary>
    public IReadOnlyList<string> Take()
    {
        lock (_sync)
        {
            if (_pending.Count == 0) return Array.Empty<string>();
            var copy = _pending.ToArray();
            _pending.Clear();
            return copy;
        }
    }

    /// <summary>某一步開始了：登記它的取消來源，讓「立刻套用」有東西可以中斷。</summary>
    internal CancellationTokenSource BeginStep(CancellationToken taskToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(taskToken);
        lock (_sync) _currentStep = cts;
        return cts;
    }

    internal void EndStep(CancellationTokenSource step)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_currentStep, step)) _currentStep = null;
        }
        step.Dispose();
    }
}

public sealed record CheckpointPrompt(string Title, string Summary);

public enum CheckpointAction
{
    Continue,
    Abort
}

public sealed record CheckpointResponse(CheckpointAction Action, string? Note = null);

/// <summary>
/// 高風險變更要不要合併，攤開給使用者判斷用的全部資料。
/// 這一關以前要使用者自己開瀏覽器去 GitHub 按，現在搬進 AITeam，
/// 所以判斷需要的東西必須一起搬進來——不然就變成盲按。
/// </summary>
public sealed record MergeApprovalPrompt(
    string ProjectName,
    int PullNumber,
    string PullUrl,
    string Version,
    string Risk,
    string ReviewMode,
    string ChangedFiles,
    string CiSummary,
    string ReviewSummary);

public enum MergeDecision
{
    /// <summary>合併。</summary>
    Merge,

    /// <summary>暫不合併：PR 與分支都保留，之後可以自己到 GitHub 處理。</summary>
    Skip
}

/// <summary>
/// 一次任務裡「使用者怎麼參與」的設定，集中成一個物件傳，
/// 免得每加一個互動方式就要在好幾層方法上多掛一個參數。
/// </summary>
/// <summary>
/// 專案還沒有 VERSION 檔時，問使用者要從哪一版開始。版號的第一碼依共用規則是使用者的決定，
/// 所以不能由 AITeam 自己挑一個數字寫進去。
/// </summary>
public sealed record StartingVersionPrompt(string ProjectName, bool IsNewProject);

/// <summary>實作的 AI（目前只處理 GPT / Codex）額度用完時，使用者的選擇。</summary>
public enum ExhaustedChoice
{
    /// <summary>停下來，工作區保留。</summary>
    Stop,

    /// <summary>換下一家 AI 接手。</summary>
    Fallback,

    /// <summary>改由 ChatGPT 聊天人工接續：AITeam 開工作單，使用者轉交，做完回來按「已完成」。</summary>
    ChatHandoff
}

/// <param name="Fallback">可以接手的下一家；null 代表沒有別家可用，只能人工接續或停止。</param>
public sealed record ImplementerExhaustedPrompt(
    ProviderId Exhausted,
    ProviderId? Fallback,
    string Step,
    string Detail);

public enum ChatHandoffAnswer
{
    /// <summary>使用者說 ChatGPT 做完了，AITeam 去 GitHub 拉回來檢查。</summary>
    Done,

    /// <summary>不做了，任務停止（分支留在 GitHub 上）。</summary>
    Cancel
}

/// <param name="Warning">上一次按「已完成」但檢查沒過的原因；第一次顯示時是 null。</param>
public sealed record ChatHandoffPrompt(
    ChatHandoffStep Step,
    int Round,
    string HandoffText,
    string WorkOrderUrl,
    string? Warning);

public sealed record TaskInteraction(
    TaskNoteBoard Notes,
    bool PauseAfterImplement,
    Func<CheckpointPrompt, CancellationToken, Task<CheckpointResponse>> AskCheckpoint,
    Func<MergeApprovalPrompt, CancellationToken, Task<MergeDecision>> AskMergeApproval,
    // 回傳 null 代表使用者不想現在決定，任務就此停下。沒有提供這個問法時維持舊行為：直接擋下。
    Func<StartingVersionPrompt, CancellationToken, Task<string?>>? AskStartingVersion = null,
    // 下面兩個沒有提供時維持舊行為：GPT / Codex 額度用完就整個任務失敗。
    Func<ImplementerExhaustedPrompt, CancellationToken, Task<ExhaustedChoice>>? AskImplementerExhausted = null,
    Func<ChatHandoffPrompt, CancellationToken, Task<ChatHandoffAnswer>>? AskChatHandoff = null)
{
    public static TaskInteraction None { get; } = new(
        new TaskNoteBoard(),
        false,
        (_, _) => Task.FromResult(new CheckpointResponse(CheckpointAction.Continue)),
        (_, _) => Task.FromResult(MergeDecision.Skip));
}
