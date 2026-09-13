# AITeam Governance Changelog

## 2.6.0 — 2026/09/13

- 共通規則升級至 2.3.0，依使用者決定將 Wade–Giles（威妥瑪）恢復並明確定義為全域共通羅馬拼音規則。
- 公司或個人 repo 都遵守「中文名稱需要羅馬拼音時使用 Wade–Giles」；特定品牌／公司／產品固定英文 spelling 仍由各 repo 自己的 `REPO_POLICY.md`／`PROJECT_RULES.md` 定義。
- 此調整不會把 CY 公司名稱、固定拼法或公司 copyright 帶入個人 sandbox。

## 2.5.0 — 2026/09/13

- 共通規則升級至 2.2.0，將母本泛化為公司／個人 repository 都可使用的中性規則。
- 公司名稱、公司固定命名／拼法、公司 copyright 與公司資料政策不再放在共通母本，改由各 repo 的 `REPO_POLICY.md` 定義。
- 個人用途 `simonliu1118-byte/sandbox` 正式納入 AITeam 共通規則同步體系；同步範圍仍只限 `REPOSITORY_RULES.md`、`COMMON_RULES_VERSION`、`COMMON_RULES_CHANGELOG.md`。
- 明確要求同步到 sandbox 的共通母本不得含 CY 公司專屬內容；sandbox 自己的個人用途、安全、copyright 與 Release 差異由 sandbox `REPO_POLICY.md` 定義。

## 2.4.0 — 2026/09/13

- 共通規則升級至 2.1.0，新增 Local-first / Token-efficient 開發原則。
- 同一工作階段避免無理由反覆完整讀取 repository；相關修正先集中於工作環境完成與本地驗證，再形成合理 commit／push 單位。
- GitHub Windows CI 定位為正式 Windows 驗收層，不再作每個微小修改的即時編譯迴圈；Windows-specific 與 Release 項目仍保留真正 Windows 驗證。
- Actions 成功時只確認 job／test／artifact 結果；失敗時先讀必要錯誤區段，原因不明才逐步擴大 log。
- 明確禁止以節省 Token 為理由省略必要 test／build／package／Release 驗證。

## 2.3.0 — 2026/09/13

- 共通規則同步不再依賴每日 GitHub Actions 排程；AITeam 母本一旦變更，同一輪治理工作即直接以 Git／GitHub API／治理 PR 同步下游 repo。
- Actions sync workflow 與 Governance Check 僅作第二道保險；Private Actions minutes 用完、停用或暫時不可用時，不得因此讓下游 repo 長期停留在舊共通規則。
- 任何 AI 接手下游 repo 前，應直接比對 `COMMON_RULES_VERSION` 與 AITeam `main`；發現落後先同步再開始 APP 工作。

## 2.2.0 — 2026/09/13

- 補上 §7 的一個缺口：明確寫出 AITeam 的 change task **完成合併後不自動建立 git tag、不自動建立／發布 GitHub Release**。
- 版本檔（VERSION／BUILD）照常在合併前更新，但 tag／Release 這個正式版身分一律等使用者另外明確觸發才建立，避免每個小修正都變成沒有意義的正式版本。
- 對應程式修改：`ChangeTaskService.RunAsync` 移除合併後自動打 tag／push tag 的步驟，`ChangeTaskResult` 也移除不再需要的 `Tag` 欄位。

## 2.1.0 — 2026/09/13

- 補上 2.0.0 移除自動 Release 後缺少的正式發布流程：新增 `.github/workflows/release-windows.yml`，以 `workflow_dispatch` 人工觸發，只能從 `main` 執行。
- 發布流程重新獨立執行 VERSION 驗證、tag 存在檢查、測試、敏感資料掃描、建置／封裝、SHA-256，再建立 tag 與 GitHub Release；不依賴先前某次 CI 成功。
- Tag／Release 一律只用 `vX.Y.Z`，不把 BUILD 次數放進 tag 名稱；Release 只在該版本真正完成時建立一次。
- 日常合併只產生短期 Actions Artifact，不建立正式 tag／Release；需要正式留存時才手動觸發發布 workflow。

## 2.0.0 — 2026/09/13

- AITeam 納入三層治理架構並成為共通規則母本，新增 `COMMON_RULES_VERSION` 與 `COMMON_RULES_CHANGELOG.md`。
- 新增 `REPO_POLICY.md`、根 `PROJECT_RULES.md`、`RULES_INDEX.md`、根 `AGENTS.md` 與 Governance Check。
- 舊 `docs/TEAM_RULES.md`、`docs/VERSIONING.md` 已正式刪除；Git history 保留歷史，不再保留平行規則入口。
- Governance Check 會阻擋新的 VERSIONING / TEAM_RULES / project-level AGENTS / 其他未授權規則入口。
- AITeam 本身與 managed repositories 的版本／Release 規則分離；操作外部 repo 時必須遵守目標 repo 的正式規則。
- 採用 GitHub private noreply commit identity 與共通 `X.Y.Z + Build N` 版本制度。
- AITeam Build workflow 改為 PR／manual 驗證與短期 Artifact，不再因 `main` push 自動 tag／Release。
- AITeam 自身 copyright 依其 `REPO_POLICY.md` 定義。
