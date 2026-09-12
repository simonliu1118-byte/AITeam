# AITeam Governance Changelog

## 2.0.0 — 2026/09/13

- AITeam 納入與 CYapps / CYapps_pvt 相同的三層治理架構。
- AITeam 成為三個 repository 的共通規則母本，新增 `COMMON_RULES_VERSION` 與 `COMMON_RULES_CHANGELOG.md`。
- 新增 `REPO_POLICY.md`、根 `PROJECT_RULES.md`、`RULES_INDEX.md`、根 `AGENTS.md` 與 Governance Check。
- 舊 `docs/TEAM_RULES.md`、`docs/VERSIONING.md` 已正式刪除；Git history 保留歷史，不再保留平行規則入口。
- Governance Check 會阻擋新的 VERSIONING / TEAM_RULES / project-level AGENTS / 其他未授權規則入口。
- AITeam 本身與 managed repositories 的版本／Release 規則分離；操作外部 repo 時必須遵守目標 repo 的正式規則。
- 採用 GitHub private noreply commit identity 與共通 `X.Y.Z + Build N` 版本制度：Major 只由使用者決定、Minor 可由 AI 依明顯功能階段判斷、Patch 為新工作項目、Build 僅用於同一項目返修。
- AITeam Build workflow 改為 PR／manual 驗證與短期 Artifact，不再因 `main` push 自動 tag／Release，避免治理或文件合併誤發布正式版本。
- Copyright notice 對齊 `Copyright © <YEAR> C.C. Liu, Chihyuan Co. All Rights Reserved.`。
