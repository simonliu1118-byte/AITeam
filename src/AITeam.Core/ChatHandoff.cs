using AITeam.Models;

namespace AITeam.Services;

public enum ChatHandoffStep
{
    Implement,
    Repair,
    CiFix
}

/// <summary>
/// GPT / Codex 額度用完時，改由 ChatGPT 聊天人工接續。
///
/// AITeam 把工作單放進任務分支，使用者把交接文字貼給 ChatGPT，ChatGPT 自己到 GitHub
/// 讀工作單、改程式、commit、push，最後把工作單的完成回報填好。使用者回到 AITeam 按
/// 「已完成」之後，挑戰、審查、版號、CI、合併都照原本的流程走。
///
/// 這裡只放「工作單長什麼樣、怎麼判斷填好了」這些不碰 git 的部分，才能單獨測試。
/// </summary>
public static class ChatHandoff
{
    public const string WorkOrderFolder = ".aiteam/work-orders";

    /// <summary>比對改動時排除工作單：那是交接用的，不是這次修改的一部分，合併前也會刪掉。</summary>
    public const string ExcludeWorkOrders = ":(exclude).aiteam/work-orders";

    private const string ReportHeading = "### 完成回報";
    private const string StatusLabel = "狀態";
    private const string DisputeLabel = "反駁";

    public static string WorkOrderPath(string taskId) => $"{WorkOrderFolder}/{taskId}.md";

    public static string WorkOrderUrl(string githubRepo, string branch, string workOrderPath) =>
        $"https://github.com/{githubRepo}/blob/{branch}/{workOrderPath}";

    public static string BranchUrl(string githubRepo, string branch) =>
        $"https://github.com/{githubRepo}/tree/{branch}";

    public static string BuildWorkOrderHeader(
        ProjectEntry project, string taskId, string branch, string request, string scoutReport, string plan)
    {
        var location = string.IsNullOrWhiteSpace(project.RepoSubpath)
            ? "整個 repository（根目錄）"
            : $"`{project.RepoSubpath.Trim('/')}`";
        var description = string.IsNullOrWhiteSpace(project.Description) ? "（未填寫）" : project.Description.Trim();
        var fresh = project.IsPendingCreation
            ? $"{Environment.NewLine}- 這是**新專案**，資料夾是這次任務才建立的，除了 VERSION 之外是空的。請參考這個 repository 其他專案的慣例從頭建立。"
            : "";

        return $"""
# AITeam 工作單：{taskId}

這份工作單由 AITeam 產生。請在分支 `{branch}` 上完成下面的工作。

## 一定要遵守

- 只在分支 `{branch}` 上 commit 和 push。不要開新分支，不要動 main 或其他分支。
- 不要合併 PR，不要改寫這個分支既有的 commit（不要 force push、不要 rebase）。
- 不要修改專案的 VERSION 檔。版號由 AITeam 在審查通過後處理。
- 開始前先讀 repository 根目錄的 AGENTS.md，照它指向的規則（REPOSITORY_RULES.md → REPO_POLICY.md → PROJECT_RULES.md）做事。
- 改完盡量跑相關的測試。
- 做完之後，填好這份工作單**最後一段**的完成回報，把「狀態：未完成」改成「狀態：已完成」，跟你的修改一起 commit、push。

## 專案

- 名稱：{project.Name}
- 說明：{description}
- 位置：{location}{fresh}

## 需求

{request.Trim()}

## 偵察結果（任務開始時整理的，程式之後可能已經變動）

{scoutReport.Trim()}

## 定案的計畫

{plan.Trim()}
""";
    }

    public static string BuildImplementSection(int handoff) => $"""

## 第 {handoff} 次交接：實作

請依照上面定案的計畫完成實作。分支上可能已經有前一個 AI 做到一半的改動，請先看過現況再接著做。

{ReportTemplate(handoff, withDispute: false)}
""";

    public static string BuildRepairSection(int handoff, int reviewRound, string challenge, string finalReview) => $"""

## 第 {handoff} 次交接：依審查意見修正（第 {reviewRound} 輪審查）

AITeam 的審查要求修正。請先看分支上目前實際的程式碼，再處理下面的意見。

挑戰者的意見：

{challenge.Trim()}

最終審查的意見：

{finalReview.Trim()}

如果你重新檢查之後確定某個意見不成立，可以不改，在完成回報的「反駁」欄寫清楚理由，AITeam 會交回審查者重新判斷。

{ReportTemplate(handoff, withDispute: true)}
""";

    private static string ReportTemplate(int handoff, bool withDispute)
    {
        var dispute = withDispute
            ? $"{Environment.NewLine}{DisputeLabel}（只有認為審查意見不成立、因此沒有修改時才填，否則留空）："
            : "";
        return $"""
{ReportHeading}（第 {handoff} 次交接，請 ChatGPT 填寫）

{StatusLabel}：未完成
改了什麼：
跑了哪些測試、結果如何：
疑慮或沒做完的地方：{dispute}
""";
    }

    /// <summary>給使用者貼進 ChatGPT 的那段話。</summary>
    public static string BuildHandoffText(
        string githubRepo, string branch, string workOrderPath, ChatHandoffStep step, string? ciSummary = null)
    {
        if (step == ChatHandoffStep.CiFix)
        {
            return $"""
請打開 GitHub repository「{githubRepo}」的分支「{branch}」，這個分支已經開了 PR，但 CI 檢查失敗。失敗摘要如下：

{(ciSummary ?? "（沒有取得失敗摘要，請直接看 PR 上的 CI 紀錄。）").Trim()}

請修正到 CI 通過，在同一個分支上 commit 並 push。
不要合併，不要開新分支，不要修改 VERSION 檔，也不要改寫這個分支既有的 commit（不要 force push）。
分支網址：{BranchUrl(githubRepo, branch)}
""";
        }

        return $"""
請打開 GitHub repository「{githubRepo}」，切到分支「{branch}」，閱讀工作單 {workOrderPath}，照工作單的指示完成工作。
完成後在同一個分支上 commit 並 push，並把工作單最後一段的完成回報填好、把「狀態：未完成」改成「狀態：已完成」。
不要合併，不要開新分支，不要修改 VERSION 檔。
工作單網址：{WorkOrderUrl(githubRepo, branch, workOrderPath)}
""";
    }

    /// <summary>
    /// 最後一段完成回報的狀態是不是「已完成」。只看最後一段：前幾次交接的回報早就是已完成了，
    /// 不能拿來當這一次的答案。容忍半形冒號、多餘空白，以及 ChatGPT 自己加的勾勾之類的字。
    /// </summary>
    public static bool IsLatestReportDone(string workOrder)
    {
        string? status = null;
        foreach (var raw in ExtractLatestReport(workOrder).Split('\n'))
        {
            var line = raw.Trim().TrimStart('-', '*', ' ').Replace("**", "");
            if (!line.StartsWith(StatusLabel, StringComparison.Ordinal)) continue;
            var colon = line.IndexOfAny(new[] { '：', ':' });
            status = colon < 0 ? "" : line[(colon + 1)..].Trim();
        }

        return status is not null && status.Contains("已完成") && !status.Contains("未完成");
    }

    /// <summary>最後一段完成回報（從最後一個「### 完成回報」到檔案結尾）。</summary>
    public static string ExtractLatestReport(string workOrder)
    {
        var text = (workOrder ?? "").Replace("\r\n", "\n");
        var at = text.LastIndexOf(ReportHeading, StringComparison.Ordinal);
        return at < 0 ? "" : text[at..].Trim();
    }

    /// <summary>
    /// 完成回報裡的「反駁」欄。有寫理由就回傳理由；沒有這一欄、留空或寫「無」都回 null。
    /// </summary>
    public static string? ExtractDispute(string report)
    {
        var lines = (report ?? "").Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim().TrimStart('-', '*', ' ').Replace("**", "");
            if (!line.StartsWith(DisputeLabel, StringComparison.Ordinal)) continue;

            var colon = line.IndexOfAny(new[] { '：', ':' });
            var first = colon < 0 ? "" : line[(colon + 1)..].Trim();
            var rest = string.Join(Environment.NewLine, lines.Skip(i + 1)).Trim();
            var reason = string.Join(Environment.NewLine, new[] { first, rest }.Where(t => t.Length > 0)).Trim();

            return reason.Length == 0 || IsNone(reason) ? null : reason;
        }

        return null;
    }

    private static bool IsNone(string text) =>
        text.Trim().Trim('（', '）', '(', ')') is "無" or "没有" or "沒有" or "none" or "None" or "N/A" or "n/a" or "-";
}
