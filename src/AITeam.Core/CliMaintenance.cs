using AITeam.Models;

namespace AITeam.Services;

public enum CliMaintenanceKind
{
    Login,
    Update
}

/// <summary>AI 狀態卡片上可以直接按的修復動作：要叫哪個 CLI 做什麼。</summary>
public sealed record CliMaintenanceAction(ProviderId Provider, CliMaintenanceKind Kind, string ButtonText, string Command, IReadOnlyList<string> Arguments, string Title);

/// <summary>
/// 登入過期、CLI 太舊這種事，不用使用者自己開 PowerShell 打指令：AITeam 開一個視窗替你執行，
/// 你只要在跳出來的瀏覽器裡登入，或等它跑完。Antigravity 沒有對應的指令，只能在它自己的程式裡處理。
/// </summary>
public static class CliMaintenance
{
    public static CliMaintenanceAction? For(ProviderId provider, ProviderHealthState state, AgentsConfig agents)
    {
        var kind = state switch
        {
            ProviderHealthState.AuthenticationRequired => CliMaintenanceKind.Login,
            ProviderHealthState.Error => CliMaintenanceKind.Update,
            _ => (CliMaintenanceKind?)null
        };
        return kind is null ? null : For(provider, kind.Value, agents);
    }

    public static CliMaintenanceAction? For(ProviderId provider, CliMaintenanceKind kind, AgentsConfig agents) =>
        (provider, kind) switch
        {
            (ProviderId.Codex, CliMaintenanceKind.Login) =>
                new(provider, kind, "登入", agents.CodexCommand, new[] { "login" }, "登入 GPT / Codex"),
            (ProviderId.Codex, CliMaintenanceKind.Update) =>
                new(provider, kind, "更新", agents.CodexCommand, new[] { "update" }, "更新 GPT / Codex"),
            (ProviderId.Claude, CliMaintenanceKind.Login) =>
                new(provider, kind, "登入", agents.ClaudeCommand, new[] { "auth", "login" }, "登入 Claude"),
            (ProviderId.Claude, CliMaintenanceKind.Update) =>
                new(provider, kind, "更新", agents.ClaudeCommand, new[] { "update" }, "更新 Claude"),
            _ => null
        };

    /// <summary>GitHub CLI 的瀏覽器登入，匯入專案、開 PR 都需要它。</summary>
    public static (string Command, IReadOnlyList<string> Arguments, string Title) GitHubLogin =>
        ("gh", new[] { "auth", "login", "--web", "--git-protocol", "https", "--hostname", "github.com" }, "登入 GitHub");
}
