# AITeam Repository Policy

本文件只定義 `simonliu1118-byte/AITeam` 的 repository-specific 規則。共通規則依根 `REPOSITORY_RULES.md`；AITeam 產品本身的固定流程依根 `PROJECT_RULES.md`。

## 1. Repository 身分

- 本 repository 為 **Public、source-visible proprietary**。
- Public visibility 不代表 open source；使用、修改、散布與商業權利以根 `LICENSE` 為準。
- 本 repository 同時是 AITeam / CYapps / CYapps_pvt 三者的**共通規則母本 repository**。

## 2. 共通規則母本責任

- `main:/REPOSITORY_RULES.md`、`main:/COMMON_RULES_VERSION`、`main:/COMMON_RULES_CHANGELOG.md` 是三 repo 共通規則唯一母本。
- 共通規則變更只能由 AITeam 的 `governance/*` branch 提出，完成 Governance Check 後合併 `main`。
- AITeam 不得藉由同步機制修改下游 repo 的 `REPO_POLICY.md` 或 `PROJECT_RULES.md`；同步範圍只限共通母本檔。
- 共通規則變更後，CYapps / CYapps_pvt 應由各自 sync workflow 建立同步 PR；在同步完成前，其 Governance Check 應阻止其他 PR 在過期共通規則下合併。

## 3. Public 安全

- 不得提交 provider API key、OAuth/token、cookies、SSH/private key、真實客戶資料、其他 repository 的秘密、D:\AITeam runtime 設定／log／tasks／repos／worktrees 內容。
- `config-templates/**` 只能保存不含秘密的範例值。
- AITeam 管理其他 repository 時取得的內容，不代表可複製到 AITeam Public repository；敏感資料仍受目標 repo 規則約束。

## 4. CI 與發行

- 一般 Build/Test workflow 應使用最小權限；沒有發布需求時不得給 `contents: write`。
- PR 自動驗證與短期工程 Artifact 可正常使用 Public Actions。
- AITeam 正式 Release／tag／自動 merge 的特殊流程若與共通規則不同，必須由根 `PROJECT_RULES.md` 明確列為例外；不得只靠 workflow 默默形成事實規則。

## 5. License / Copyright

- Copyright notice 使用：`Copyright © <YEAR> C.C. Liu, Chihyuan Co. All Rights Reserved.`
- `<YEAR>` 依正式發布年份處理。
