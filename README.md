# AITeam

AITeam is a Windows-oriented local orchestration tool for coordinating multiple AI coding assistants, Git repositories, isolated worktrees, verification, review, and versioned delivery.

## Current status

The repository currently preserves the working PowerShell/WinForms prototype while the next phase moves toward a portable Windows executable.

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

The portable EXE rewrite starts at `v0.2.0-alpha.1`.

The first alpha intentionally runs side-by-side with the existing PowerShell prototype. It validates the native executable shell, project-registry compatibility, provider health checks, hidden subprocess execution, and GitHub Actions self-contained Windows publishing before any source-writing task pipeline is moved into the EXE.

See `docs/EXE_MIGRATION_PLAN.md`.
