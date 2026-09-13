using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public sealed class TaskNoteBoardTests
{
    [Fact]
    public void NotesQueueUp_AndAreTakenOnlyOnce()
    {
        var board = new TaskNoteBoard();
        board.Add("順便把按鈕改成藍色", interruptNow: false);
        board.Add("測試也要一起補", interruptNow: false);

        Assert.True(board.HasPending);
        Assert.Equal(new[] { "順便把按鈕改成藍色", "測試也要一起補" }, board.Take());
        Assert.False(board.HasPending);
        Assert.Empty(board.Take());
    }

    [Fact]
    public void BlankNotes_AreIgnored()
    {
        var board = new TaskNoteBoard();
        board.Add("   ", interruptNow: false);
        board.Add("", interruptNow: true);

        Assert.False(board.HasPending);
    }

    [Fact]
    public void ApplyNow_CancelsTheStepInFlight()
    {
        var board = new TaskNoteBoard();
        var step = board.BeginStep(CancellationToken.None);

        board.Add("停一下，方向錯了", interruptNow: true);

        Assert.True(step.Token.IsCancellationRequested);
        Assert.True(board.HasPending);
        board.EndStep(step);
    }

    [Fact]
    public void QueuedNote_DoesNotCancelTheStepInFlight()
    {
        var board = new TaskNoteBoard();
        var step = board.BeginStep(CancellationToken.None);

        board.Add("等這步跑完再說", interruptNow: false);

        Assert.False(step.Token.IsCancellationRequested);
        board.EndStep(step);
    }

    [Fact]
    public void ApplyNow_AfterTheStepEnded_JustQueuesInsteadOfThrowing()
    {
        var board = new TaskNoteBoard();
        var step = board.BeginStep(CancellationToken.None);
        board.EndStep(step);

        board.Add("補充一句", interruptNow: true);

        Assert.True(board.HasPending);
    }

    [Fact]
    public void CancellingTheWholeTask_AlsoCancelsTheCurrentStep()
    {
        using var task = new CancellationTokenSource();
        var board = new TaskNoteBoard();
        var step = board.BeginStep(task.Token);

        task.Cancel();

        Assert.True(step.Token.IsCancellationRequested);
        board.EndStep(step);
    }
}
