using AITeam.Models;

namespace AITeam.Services;

public enum TaskKind
{
    Inquiry,
    Change,

    /// <summary>AI 四方會議。不走階段條，但會跟其他任務一起留在歷史紀錄裡。</summary>
    Meeting
}

public static class TaskKindExtensions
{
    public static string ToFriendlyName(this TaskKind kind) => kind switch
    {
        TaskKind.Inquiry => "查詢",
        TaskKind.Change => "修改",
        TaskKind.Meeting => "會議",
        _ => kind.ToString()
    };
}

public enum TaskStage
{
    Prepare,
    Inquire,
    Scout,
    Plan,
    Implement,
    Review,
    Verify,
    Merge
}

public enum TaskActivity
{
    Running,
    WaitingForUser,
    Completed,
    Failed
}

/// <summary>
/// 任務目前走到哪一階段的結構化回報，供畫面畫階段進度用；
/// 純文字的逐行 log 仍然照舊由 progress 回呼輸出，兩者互不取代。
/// </summary>
public sealed record TaskProgress(
    TaskKind Kind,
    TaskStage Stage,
    TaskActivity Activity,
    string Detail,
    ProviderId? Provider = null,
    int Round = 0);

public static class TaskStages
{
    private static readonly TaskStage[] InquiryStages =
    {
        TaskStage.Prepare,
        TaskStage.Inquire
    };

    private static readonly TaskStage[] ChangeStages =
    {
        TaskStage.Prepare,
        TaskStage.Scout,
        TaskStage.Plan,
        TaskStage.Implement,
        TaskStage.Review,
        TaskStage.Verify,
        TaskStage.Merge
    };

    public static IReadOnlyList<TaskStage> For(TaskKind kind) => kind switch
    {
        TaskKind.Change => ChangeStages,
        // 會議沒有階段可言（輪流發言而已），借用查詢的兩格，讓畫面不會空掉。
        _ => InquiryStages
    };

    public static string DisplayName(TaskStage stage) => stage switch
    {
        TaskStage.Prepare => "準備",
        TaskStage.Inquire => "查詢",
        TaskStage.Scout => "調查",
        TaskStage.Plan => "規劃",
        TaskStage.Implement => "實作",
        TaskStage.Review => "審查",
        TaskStage.Verify => "驗證",
        TaskStage.Merge => "合併",
        _ => stage.ToString()
    };
}
