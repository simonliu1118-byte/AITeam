# AITeam architecture

## Product direction

AITeam is intended to become a portable Windows application distributed as a ZIP. The user should be able to extract the folder and run `AITeam.exe` without installing a development runtime.

## Current prototype

The current implementation is PowerShell + WinForms and invokes:

- `git`
- `codex`
- `claude`
- `agy`

The prototype remains operational while the executable version is developed.

## Change task pipeline

1. Intent classification
2. Gemini / Antigravity scout + draft plan
3. GPT / Codex plan gate + risk judgment
4. Claude implementation + focused tests
5. Verification
6. Gemini / Antigravity challenge
7. GPT / Codex final review
8. Claude repair when required
9. Version bump
10. Commit
11. Merge to main
12. Tag
13. Push to GitHub

LOW / NORMAL / HIGH use the same roles. Risk changes review depth, not which providers participate.

## Inquiry path

Inquiry-only work must remain read-only and must not create a worktree, version, commit, tag, or push.

## Repository model

AITeam project != Git repository.

A monorepo such as `CYapps` may contain multiple AITeam projects. Local clone locations are allocated by AITeam; the user chooses GitHub repository + repository subpath.
