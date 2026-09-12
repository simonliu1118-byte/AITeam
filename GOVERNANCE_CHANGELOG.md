# AITeam Governance Changelog

## 2.1.0 — 2026/09/13

- 補上 2.0.0 移除自動 Release 後缺少的正式發布流程：新增
  `.github/workflows/release-windows.yml`，以 `workflow_dispatch` 人工觸發，
  只能從 `main` 執行。
- 發布流程會重新獨立執行：讀取並驗證 `VERSION`、確認對應 tag 尚未存在、
  重新跑一次 `AITeam.Core.Tests`、掃描已追蹤原始碼是否有明顯的金鑰／
  Token／私鑰樣式、重新建置與封裝、計算 zip 的 SHA-256，最後才建立 tag
  與 GitHub Release（附上 zip 與 `.sha256` 檔）。不依賴先前任何一次 CI
  的結果。
- 依使用者決定：Tag／Release 一律只用 `vX.Y.Z`，不把 `BUILD` 次數放進
  tag 名稱——`BUILD` 代表「同一件事還沒修好、重複返修」，Release 只在
  該版本真正完成時才建立一次，因此不需要每個 Build 都有自己的 tag。
- 日常合併（PR 驗證）產生的仍是短期 Actions Artifact，不建立正式 tag／
  Release；需要正式留存的版本才手動觸發 `release-windows.yml`。

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
