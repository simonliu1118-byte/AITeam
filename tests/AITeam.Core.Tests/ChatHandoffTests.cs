using AITeam.Models;
using AITeam.Services;
using Xunit;

namespace AITeam.Core.Tests;

public class ChatHandoffTests
{
    private static readonly ProjectEntry Project = new()
    {
        Name = "CYInvoice", RepoSubpath = "apps/cyinvoice", GitHubRepo = "owner/repo"
    };

    private static string WorkOrder() =>
        ChatHandoff.BuildWorkOrderHeader(Project, "t1", "aiteam/task-t1", "加上匯出功能", "偵察內容", "計畫內容")
        + ChatHandoff.BuildImplementSection(1);

    [Fact]
    public void FreshWorkOrder_IsNotDone()
    {
        Assert.False(ChatHandoff.IsLatestReportDone(WorkOrder()));
    }

    [Theory]
    [InlineData("狀態：已完成")]
    [InlineData("狀態: 已完成")]
    [InlineData("- **狀態：** 已完成 ✅")]
    public void FilledWorkOrder_IsDone_EvenWhenChatGptFormatsItDifferently(string statusLine)
    {
        var filled = WorkOrder().Replace("狀態：未完成", statusLine);

        Assert.True(ChatHandoff.IsLatestReportDone(filled));
    }

    [Fact]
    public void OnlyTheLatestReportCounts()
    {
        // 第一次交接早就填好了，第二次交接（審查修正）還沒填：不能拿第一次的答案當這次的。
        var text = WorkOrder().Replace("狀態：未完成", "狀態：已完成")
                   + ChatHandoff.BuildRepairSection(2, 1, "挑戰意見", "審查意見");

        Assert.False(ChatHandoff.IsLatestReportDone(text));
    }

    [Fact]
    public void WorkOrderSpellsOutTheGroundRules()
    {
        var text = WorkOrder();

        Assert.Contains("aiteam/task-t1", text);
        Assert.Contains("不要修改專案的 VERSION 檔", text);
        Assert.Contains("不要合併 PR", text);
        Assert.Contains("AGENTS.md", text);
        Assert.Contains("apps/cyinvoice", text);
    }

    [Fact]
    public void DisputeIsReadFromTheRepairReport()
    {
        var text = ChatHandoff.BuildRepairSection(2, 1, "挑戰意見", "審查意見")
            .Replace("否則留空）：", "否則留空）：這個檔案早就有做空值檢查，第 42 行。");

        Assert.Equal("這個檔案早就有做空值檢查，第 42 行。", ChatHandoff.ExtractDispute(ChatHandoff.ExtractLatestReport(text)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("無")]
    [InlineData("（無）")]
    public void EmptyOrNoneDispute_MeansNoDispute(string value)
    {
        var text = ChatHandoff.BuildRepairSection(2, 1, "挑戰意見", "審查意見")
            .Replace("否則留空）：", "否則留空）：" + value);

        Assert.Null(ChatHandoff.ExtractDispute(ChatHandoff.ExtractLatestReport(text)));
    }

    [Fact]
    public void HandoffTextPointsAtTheBranchAndWorkOrder()
    {
        var text = ChatHandoff.BuildHandoffText("owner/repo", "aiteam/task-t1", ChatHandoff.WorkOrderPath("t1"), ChatHandoffStep.Implement);

        Assert.Contains("owner/repo", text);
        Assert.Contains("aiteam/task-t1", text);
        Assert.Contains("https://github.com/owner/repo/blob/aiteam/task-t1/.aiteam/work-orders/t1.md", text);
    }

    [Fact]
    public void CiHandoffCarriesTheFailureInsteadOfAWorkOrder()
    {
        var text = ChatHandoff.BuildHandoffText(
            "owner/repo", "aiteam/task-t1", ChatHandoff.WorkOrderPath("t1"), ChatHandoffStep.CiFix, "test 失敗：NullReferenceException");

        Assert.Contains("NullReferenceException", text);
        Assert.DoesNotContain("work-orders", text);
    }

    [Fact]
    public void CodexQuotaMessage_IsFoundEvenBehindTheVersionBanner()
    {
        // Codex 的錯誤輸出第一行常常是版本橫幅，額度那句話在後面。
        var output = "OpenAI Codex v0.50.0 (research preview)\n--------\nworkdir: C:\\x\nERROR: You've hit your usage limit. Try again in 3 hours.";

        Assert.True(ProviderHealthService.LooksLikeQuota(output));
        Assert.Equal("ERROR: You've hit your usage limit. Try again in 3 hours.", ChangeTaskService.QuotaLine(output));
    }

    [Fact]
    public void SwitchingToAReviewerAsFixer_ForcesHumanApproval()
    {
        var risk = ChangeTaskService.RaiseRiskIfSelfReviewed(
            ProviderId.Claude, new[] { ProviderId.Antigravity, ProviderId.Claude }, ChangeRisk.Normal, _ => { });

        Assert.Equal(ChangeRisk.High, risk);
    }

    [Fact]
    public void SwitchingToSomeoneOutsideTheReview_KeepsTheRisk()
    {
        var risk = ChangeTaskService.RaiseRiskIfSelfReviewed(
            ProviderId.Claude, new[] { ProviderId.Antigravity, ProviderId.Codex }, ChangeRisk.Low, _ => { });

        Assert.Equal(ChangeRisk.Low, risk);
    }
}
