# AITeam Operating Rules — Fixed Three-AI Pipeline

1. The user describes the desired task; AITeam handles routing, Git isolation, implementation, review, repair, versioning, and normal GitHub synchronization.
2. Intent is decided before repository analysis. Ambiguous requests default to inquiry, not permission to modify.
3. Inquiry tasks are read-only and do not create a worktree, call Claude for implementation, bump versions, commit, merge, tag, or push.
4. Every confirmed CHANGE task uses the same pipeline regardless of low/normal/high risk: Gemini/Antigravity Scout + Draft Plan -> GPT Plan Gate + formal Risk -> Claude implementation -> verification -> Gemini/Antigravity independent challenge -> GPT Final Review.
5. Risk changes rigor/depth only. It never skips Gemini, GPT, or Claude from a change task.
6. Gemini/Antigravity Scout gathers evidence and proposes a draft plan. Its claims are evidence to verify, not authority.
7. GPT/Codex verifies the Scout report against source, corrects it when necessary, decides the formal risk level, approves the final implementation plan, and makes the final technical acceptance decision.
8. Claude Code is the primary implementer and repairer for all change tasks.
9. Implementation never edits the registered main working tree directly. Every write task uses an isolated Git branch/worktree under D:\AITeam\worktrees.
10. Gemini/Antigravity Scout and Challenger stages are read-only. Any detected modification during those stages stops the task.
11. A completed task is one releasable small version. Internal repair rounds do not create extra public versions.
12. After verification/review pass, AITeam updates the project version, commits, merges to the default branch, tags the version, and pushes the default branch + tag to GitHub automatically.
13. Normal push/merge/tag of a completed version is allowed. Force-push, history rewrite, remote deletion, release publishing, and deployment remain forbidden.
14. Git history and GitHub are the permanent source of truth. AI chat/session state is not source control.
15. Failed tests/builds must never be reported as passed. Optional missing local tools are reported as skipped, not passed.
16. Do not read or place passwords, API keys, OAuth tokens, cookies, customer runtime data, production credentials, or other secrets in prompts, logs, commits, or task artifacts.
17. Do not refactor unrelated working code. Changes must stay inside the registered project subtree.
18. Repair loops stop after the configured maximum. On failure, preserve the isolated task/worktree for diagnosis rather than damaging main.

## Multi-AI availability / fallback
- GPT/Codex, Claude and Gemini/Antigravity each have an independent availability state.
- A provider that hits quota/usage/rate limits is marked cooldown and skipped until the user retries it.
- Authentication, temporary provider/network errors, and CLI failures are recorded separately.
- Preferred roles remain fixed, but unavailable providers are replaced by the configured fallback order.
- Read-only stages must not mutate the worktree; mutation during a read-only role is a safety stop, not a fallback condition.
- A change may continue in degraded mode, but formal Merge/Tag/Push requires at least two distinct engineering agents and an independent reviewer distinct from the implementer.
- Manual provider pause is allowed from the AITeam GUI.


15. Manual provider deselection is session-only and must never persist across AITeam restarts.
16. Only quota/usage limits and authentication failures persist as provider-unavailable states. Generic CLI/provider/format/network failures must leave a provider error log and fall back without permanently disabling that AI.
17. Provider diagnostic logs live under D:\AITeam\logs\providers and survive successful task cleanup.
18. Project registration is separate from Git repository storage: one repository may contain multiple AITeam projects through repo_subpath. Removing a project registration must never delete source code, Git history, or a remote repository.
