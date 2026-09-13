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
/// 一次任務裡「使用者怎麼參與」的設定，集中成一個物件傳，
/// 免得每加一個互動方式就要在好幾層方法上多掛一個參數。
/// </summary>
public sealed record TaskInteraction(
    TaskNoteBoard Notes,
    bool PauseAfterImplement,
    Func<CheckpointPrompt, CancellationToken, Task<CheckpointResponse>> AskCheckpoint)
{
    public static TaskInteraction None { get; } = new(
        new TaskNoteBoard(),
        false,
        (_, _) => Task.FromResult(new CheckpointResponse(CheckpointAction.Continue)));
}
