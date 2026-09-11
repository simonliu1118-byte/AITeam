# Portable EXE migration plan

## Phase 1 — v0.2.0-alpha.1

Goal: establish the real Windows executable foundation without replacing the working PowerShell prototype.

Included:

- .NET 8 WinForms `WinExe` (no console window)
- self-contained `win-x64` folder publish
- single-window split UI foundation
- reads existing AITeam project registry from:
  1. `D:\AITeam\data\projects.json`
  2. legacy `D:\AITeam\config\projects.json`
- startup provider health probes for:
  - GPT / Codex
  - Claude
  - Gemini / Antigravity
- manual provider checkbox state is session-only
- subprocesses use redirected stdout/stderr and `CreateNoWindow`
- no project source writes
- no task/worktree/version/Git orchestration yet

The PowerShell prototype remains the operational implementation while this alpha is validated.

## Phase 2

- port GitHub-first Project Manager
- migrate runtime configuration from `config` to `data`
- add Inquiry mode in EXE
- preserve provider error logs

## Phase 3

- port fixed three-AI change pipeline
- isolated worktree lifecycle
- verification / challenge / final review / repair loop
- version + commit + merge + tag + push

## Phase 4

- Task Planning Chat + Task Contract
- resumable task state

## Phase 5

- AI Meeting
- multi-agent structured discussion
- meeting decision -> one or more Tasks

## Final deployment structure

```text
D:\AITeam\
├─ AITeam.lnk
├─ current\      # portable built app
├─ source\       # AITeam Git source
├─ data\         # machine-local configuration
├─ repos\
├─ worktrees\
├─ tasks\
├─ logs\
├─ temp\
└─ legacy\       # retired PowerShell prototype
```

The final migration is performed only after the EXE can safely read existing settings and operate alongside the prototype.
