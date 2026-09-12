# AITeam

AITeam is a Windows-oriented local orchestration tool for coordinating multiple AI coding assistants, Git repositories, isolated worktrees, verification, review, and versioned delivery.

## Current status

The portable Windows executable (`src/AITeam.App`) is now the primary implementation: it runs the full inquiry/change-task pipeline described below. The original PowerShell/WinForms prototype under `prototype/` is kept only for historical reference and is no longer the operational path.

## Current AI roles

- Gemini / Antigravity: repository scout, evidence gathering, draft plan, independent challenge
- GPT / Codex: plan gate, risk judgment, final technical review
- Claude Code: primary implementation, tests, repair

All change tasks follow the same fixed pipeline. Provider availability/fallback logic can temporarily skip an unavailable provider.

## Runtime layout

The Git repository is intentionally separated from machine-specific runtime data.

- `D:\AITeam\source` — this repository / development source
- `D:\AITeam\repos` — managed project repositories
- `D:\AITeam\worktrees` — temporary isolated task worktrees
- `D:\AITeam\tasks` — active/failed task artifacts
- `D:\AITeam\logs` — provider/startup/history logs
- `D:\AITeam\config` — current machine runtime configuration

Runtime data, credentials, provider states, and managed repositories are not committed here.

## Portable Windows EXE development

The portable EXE rewrite started at `v0.2.0-alpha.1` and has, as of `v0.5.0`, absorbed the full guarded change-task pipeline (Scout → Plan Gate → Implement → Verify → Challenge → Final Review → Repair → version/commit/tag/push). See `docs/EXE_MIGRATION_PLAN.md` for the phased history and `docs/V0.4.0.md` / `docs/V0.5.0.md` for what shipped in each release.
