# AITeam Repository Policy

本文件只定義 `simonliu1118-byte/AITeam` 的 repository-specific 規則。共通規則依根 `REPOSITORY_RULES.md`；AITeam 產品本身的固定流程依根 `PROJECT_RULES.md`。

## 1. Repository 身分

- 本 repository 為 **Public、source-visible proprietary**。
- Public visibility 不代表 open source；使用、修改、散布與商業權利以根 `LICENSE` 為準。
- 本 repository 是目前治理體系的**共通規則母本 repository**；目前納管 repo 包含 `AITeam`、`CYapps`、`CYapps_pvt`、`chihyuan-web` 與個人用途的 `sandbox`。

## 2. 共通規則母本責任

- `main:/REPOSITORY_RULES.md`、`main:/COMMON_RULES_VERSION`、`main:/COMMON_RULES_CHANGELOG.md` 是納管 repositories 的共通規則唯一母本。
- 共通規則變更只能由 AITeam 的 `governance/*` branch 提出；AITeam `main` 更新後，同一輪治理工作必須直接把這三個共通檔同步到所有下游納管 repo，全部一致後才視為共通規則變更完成。
- 直接同步可使用正常 Git／GitHub API／治理 PR，不得依賴 GitHub Actions schedule 才能完成；Actions 額度不足、停用或暫時不可用時，仍必須完成同步。
- AITeam 不得藉由同步機制修改下游 repo 的 `REPO_POLICY.md` 或 `PROJECT_RULES.md`；同步範圍只限共通母本檔。
- 下游 sync workflow 與 Governance Check 是第二道自動化保險，不是唯一一致性來源。
- 任何 AI 接手下游納管 repo 工作前，應直接比對其 `COMMON_RULES_VERSION` 與 AITeam `main`；若版本不同或內容有疑義，先同步共通規則，再開始 APP 工作。
- `sandbox` 是個人用途 repository；AITeam 同步到 sandbox 的共通母本必須保持中性，不得把 CY 公司名稱、公司命名規則、公司 copyright 或公司資料政策寫進共通母本。

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
