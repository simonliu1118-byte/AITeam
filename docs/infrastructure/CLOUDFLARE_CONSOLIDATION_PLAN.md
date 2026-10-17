# CY 系列跨 Repository Cloudflare／GitHub 整併主控計畫

> 建立：2026-10-12（Asia/Taipei）。**AITeam 是本次跨 Repo 工作的唯一主控／進度來源；本檔是一次性工作計畫，不是第四層永久規則，不取代任何目標 Repo 的 REPOSITORY_RULES.md／REPO_POLICY.md／PROJECT_RULES.md。**
>
> 管理範圍：simonliu1118-byte/CYapps（Public）、simonliu1118-byte/CYapps_PVT（Private）、simonliu1118-byte/chihyuan-web（Public）。AITeam 本身是協調文件的儲存庫，不算第四個待整併的產品部署。
>
> 目標：採**一套正式 Cloudflare 運行環境**＋Local Wrangler/D1 與 GitHub CI 作為主要開發測試方式，避免為每個小程式建立 Dev／Production 成對 Worker/D1。Existing Dev 有實際 Employee／Workspace／Device／業務資料者，在真正完成遷移及驗收前必須保留，不依名稱推斷可刪除。

> **最新基準：2026-10-18（Asia/Taipei），見 §10.10；§10.7～10.9 為前批部署與備份準備紀錄。** CYACC #475 已合併，合併／發布分離已生效，首次新模式發布仍待驗收；CY Web 與 CYACC 已共用正式 CYID。CY Web [HTTPS #180](https://github.com/simonliu1118-byte/chihyuan-web/pull/180) 與 [發布護欄 #178](https://github.com/simonliu1118-byte/chihyuan-web/pull/178) 已合併，HTTPS **0.8.2** 的驗收見 §10.7；目前含 Claude 最新登入修正的 **0.8.7 Build1／active72** 已成功部署，固定隔離還原入口已接線。GCS Worker原生fetch相容性錯誤已修正並完成實際workerd回歸；首筆備份先前的GCS失敗已由同事件retry補齊；**真實雙端verified與GCS隔離還原97筆均已通過**，排程依使用者要求維持false，待整體測試完成、正式上線前提醒啟用，見 §10.10。第 1、6、7、8 節為歷史盤點／方案，舊狀態與執行順序不直接作為目前待辦。本檔仍為唯一主控。

## 0. 使用者已定案的限制與目的

1. **CYACCweb 改採與 CY Web 相同的合併／發布分離。** 使用者 2026-10-16 新決定取代 10/12「保留 main 自動部署」：PR 自動驗證，main 合併不發布；Work 推進受控發布分支或從 main dispatch。CYapps [PR #475](https://github.com/simonliu1118-byte/CYapps/pull/475) 已於 10/17 02:17（Asia/Taipei）合併至 `127acb7`，main 不再自動部署；首次新模式的正式發布仍待當次指示與驗收。保留正式 login／資產 smoke，不新增人工 Reviewer／wait timer 或測試資源。
2. **不再建立、上傳、刪除重試遠端測試 Worker／probe Version／清理 handler。** CYInvoice 過去遇到 GET 仍可讀取、DELETE 404／10339，無法安全確認已清理。保留歷史證據、停止遠端測試程式部署、停止額外權限擴張；Local/CI synthetic tests 正常保留。
3. 目標「單一正式雲端」是**邏輯環境收斂**，不是把所有 App 及 D1 合成一個。CYID 的 Identity D1 與各 App 業務 D1 仍依責任分離；小 App 無雲端資料需求不需 D1。
4. CYInvoice 的 IDENTITY（Fetcher／Login、Resolve、Logout）和 IDENTITY_AUTHORITY（同 CYID Worker 的 ConsumerAuthoritySync Named RPC，用於離線 Employee 快取失效比對）不是兩個 Worker 或 D1；目前維持兩個入口。
5. CYInvoice 原 Device、Device Token、Workspace、發票業務、歷史與原 Cloud D1 必須保留；CYID Development／Production 的 Employee、Credential、Role、App Access、Session 也不可直接覆蓋合併。
6. 文件先進 AITeam PR。各目標 Repo 執行時仍走各自 branch／PR／CI。**本計畫不等於已批准直接做正式身分切換、D1 資料遷移、Worker／D1 刪除、Secret 重設或 Release。** 進到此類操作前必須完成該階段 Gate 並核對可回復證據。

## 1. 初始唯讀來源盤點（2026-10-12 歷史，不是目前即時清冊）

| Repo／系統 | GitHub main 目前可確認事項 | 原則／尚待驗證 |
| --- | --- | --- |
| CYapps／CYID | cycloudidentity-deploy.yml、cycloudidentity-production-deploy.yml 是兩條部署線。Production workflow 目前仍使用 GitHub development Environment，且由 Dev Worker Name 推導正式 Worker Name；正式 D1 有獨立性檢查。 | 既有 CYID Production 優先當統一 authority 候選，但尚未對照 Dev 與 Prod 的帳號/權限差異，不得切換、停用或刪除任何一邊。 |
| CYapps／CYACCweb | cyaccountingweb-deploy.yml 針對 main push 自動 Validate → Deploy；其 deployment job 目前稱 development，實際有正式 Custom Domain、CYID Production resolver 與業務 D1/R2。 | **保留 main 自動部署**；日後整理 Environment/Variables 不能中斷。要核對域名、真正的 Worker/D1/Backup 及 Service Binding。 |
| CYapps／CYInvoice | cyinvoice-cloud-dev-deploy.yml、cloud/scripts/deployment-preflight.mjs 目前管理原有 Worker/D1。2026-10-12 既有 Development Worker 已以 activate-cyid 啟用 CYID，保留原一個 Workspace、四筆 Device records、Token hashes／業務歷史及 FK。 | 使用者表示目前連接看似正常；仍須核對原 Windows Device/Employee App Access、實際 Binding 與業務連續性，不把 Source／部分驗證誤作完整正式切換完成。 |
| chihyuan-web／CY Web | 獨立 Public Repo，development-deploy.yml 可手動 dispatch 或由受控 deploy branch 部署；有自身業務 D1、主要管理 Domain 與 CYID Binding。 | 核對真實 Domain 指向、業務資料權威和當前 Service Binding；不要直接憑 workflow 名稱判斷是測試服務。 |
| CYapps_PVT／CYWatermark | GitHub main 可看到 cywatermark-build.yml；沒有發現 Cloudflare deploy workflow。此 Private Repo 目前正式維護 CYWatermark。 | **Private Repo 也在主控範圍。** 不能假設無 workflow 就完全沒有手動管理的雲端資源；不拷貝 Private 程式、秘密或 key 到 Public AITeam。 |

版本參考（僅建立當時 Source）：CYID 0.3.7／Consumer Contract 1.0.4（minimum 1.0.0）；CYInvoice 2.6.19／CYID Consumer 1.0.3。當實作開始需重新檢查 deployed source 與 runtime，不將此列當固定值。

證據 Owner：
- CYID：https://github.com/simonliu1118-byte/CYapps/blob/main/apps/CYCloudIdentity/docs/DEVELOPMENT_DEPLOYMENT.md
- CYInvoice：https://github.com/simonliu1118-byte/CYapps/blob/main/apps/CYInvoice/docs/CLOUD_WORK_HANDOFF.md
- CYID Consumer：https://github.com/simonliu1118-byte/CYapps/blob/main/apps/CYCloudIdentity/docs/CONSUMER_INTEGRATION_STANDARD.md
- CYACC：https://github.com/simonliu1118-byte/CYapps/blob/main/.github/workflows/cyaccountingweb-deploy.yml
- CY Web：https://github.com/simonliu1118-byte/chihyuan-web/blob/main/.github/workflows/development-deploy.yml
- CYapps_PVT Repo：https://github.com/simonliu1118-byte/CYapps_PVT

## 2. 長期目標與非目標

### 目標
- CYID 保留**一套正式 Identity Worker/D1**；CY Web／CYACCweb／CYInvoice 指向同一個可查證的正式身份服務，依各自 App Access 及 Workspace mapping 工作。
- 每個需要雲端業務資料的 App 維持既有 Worker／業務 D1；保留 R2/cron 或其他實際有用的元件。CYapps_PVT 如無雲端需要，無須新建。
- 遠端常駐 Dev／probe 不再作日常測試依賴；使用 Local Wrangler／D1 和 GitHub CI 驗證，再把**已審核的實際程式碼**部署到既有運行資源。
- GitHub Variables/Secrets 分成：可公開的 source placeholder、Environment 中的部署 metadata、Secrets 中的憑證、Cloudflare Runtime Secrets。每個值有唯一 owner；減少 Dev 名稱推導 Production、跨 App 同名泛用變數與重複拷貝。
- 實體 Worker 名稱含 dev 不等於可刪；是否需要改名要另外評估「保留原服務、避免 Device/Session 斷線」與清楚命名的取捨。

### 非目標
- 不藉機重構 CYID Session／RBAC／Email 邏輯、CYInvoice Device/Offline 架構或 CYACCweb 記帳業務。
- 不把 GitHub Environment Reviewer 強迫套用到 CYACC；依使用者新決定採受控分支／main dispatch，操作指令看 CYACC README。
- 不重啟過去刪不掉的 Worker/Version 清理專案，也不因本輪打開高權限 Cloudflare Token。
- 不在 Public AITeam Git 中保存真實 Worker Name、D1 UUID、Workspace/Employee ID、Customer/Invoice data 或 Private Repo Key。

## 3. GitHub Environment／Variables／Secrets 整理方案

**此表保留初始 source 引用與過渡建議；第 7.4 節僅為 10/12 歷史 UI 證據，目前名稱／scope API 受 403 限制。現行過渡及執行順序見第 9.3／9.4 節，不照第 8 節舊方案搬移。引用存在不代表值或目標正確。**

| Repo／用例 | 原始碼可確認的引用 | 建議 |
| --- | --- | --- |
| CYapps／CYID development | secrets.CLOUDFLARE_API_TOKEN、secrets.CLOUDFLARE_ACCOUNT_ID、secrets.CYID_WORKER_NAME、vars.CYID_D1_DATABASE_ID | 此舊來源先保留，直到跨環境權威差異與安全切換完成；Provider 原正式/開發 Binding 必須讀回核對。 |
| CYapps／CYID Production | 仍使用 environment: development；由既有 CYID_WORKER_NAME 推導 Production，另有 vars.CYID_PRODUCTION_CORE_CONSUMER_WORKER_NAME | 將正式目標改為**明確的來源**，不靠 Dev 字尾推導；以原 Cloudflare 配置 read-back 做嚴格比對，確定無誤才遷移 GitHub Environment。 |
| CYapps／CYACCweb | vars.CF_WORKER_NAME、CF_D1_DATABASE_NAME/ID、CF_R2_BACKUP_BUCKET、CF_BACKUP_TOPOLOGY、CF_CYACCOUNTINGWEB_CUSTOM_DOMAIN；secrets.CYID_WORKER_NAME 與 Cloudflare credentials | #475 已合併，合併／發布分離已生效，首次新入口發布待驗收；development scope 暫留。先核對名稱、scope 與原目標映射，才搬移；不重建或重設既有 Secret。 |
| CYapps／CYInvoice | secrets.CYID_WORKER_NAME、Cloudflare credentials；Workspace mapping 與 scope 從 Provider／original business D1 read-back | 不把真實 Employee/Workspace/Device secrets 作第二份 GitHub 真值；保留 CYID_ENABLED 與原有 D1 Device continuity。 |
| chihyuan-web／CY Web | vars.CF_WORKER_NAME、CF_D1_DATABASE_NAME/ID、CF_IDENTITY_SERVICE、CF_IDENTITY_APPLICATION_ID、CF_IDENTITY_WORKSPACE_ID、CF_BACKUP_*、GCS_BUCKET；相應 Cloudflare/Backup Secrets | 本 Repo GitHub Environment 與 CYapps 獨立；選定最終 CYID 目標後再收斂自身 Deployment Metadata。 |
| CYapps_PVT | main 無 Cloudflare deploy workflow | 不先建立空的 Cloudflare Variable/Environment，只檢查是否存在實際手動／外部部署。 |

**已完成的歷史工作不是新的 TODO：** 2026-10-12 既有文件記載使用者已刪除 development Environment 與 Secret 同名的 CYID_WORKER_NAME Variable，保留 Secret，並在 CYInvoice read-only preflight 通過。不得又要求新建同名 Variable 或推定它仍待清理。

### Production Environment 與受控發布

- 最終建議按 Repo 建立清楚的正式部署環境邊界，但不強迫每 App 有成對的長期 Dev+Prod。
- 本輪保留現有 Environment scope。未來 production scope 必須同時容許 main dispatch 與已核准的受控發布分支；只允許 main 會阻擋分支入口。搬移前核對 metadata 仍指向原 Worker／業務 D1，並保留各 App runtime read-back gate。
- 之後若特別要為 CYID 設 GitHub Environment 內建人工 Reviewer，再拆出**少量必要**的 CYID 專用 Protected Environment；CYACC 不搬入需要人工核准的環境。
- CYapps 多 App 共用 Environment 時，CF_WORKER_NAME／CF_D1_DATABASE_ID 等**泛名可能互相覆蓋**。若統一 Environment，需使用 per-App 前綴變數或確定版本的 Runtime Read-back；不應一邊改名字一邊讓 CYACC 自動部署到不明目標。
- Cloudflare Runtime Secrets 以保留原值、檢查存在與類型為主。GitHub 的 Secret 只有部署必要時才存，不在 Public Repo 或 log 直接回顯；權限縮減／金鑰輪替是後續另外評估項。

## 4. 階段、Gate 與可回復條件

狀態只用 DONE / IN_PROGRESS / READY / BLOCKED / PENDING。任何執行者先讀本文件**最新 main**、再按目标 Repo 規則與現況核對，不能用對話記憶替代。每個階段結束要更新本文件的證據與下一步。

| ID | 狀態 | 工作 | 驗證 Gate | 安全限制／回復 |
| --- | --- | --- | --- | --- |
| P0 | DONE | 在 AITeam 建立這份跨 Repo 主控文件及追蹤入口 | PR 只有 docs 修改，沒有更新產品版本／規則／Workflow／Secrets | 可撤回 PR，不影響現行部署 |
| P1 | IN_PROGRESS | 已回讀 5 Workers／8 D1／2 R2（10/18增Web隔離還原D1，見§10.10）、active bindings 與 Builds；GitHub 名稱 API 403，owner／外部依賴／回復仍有缺口 | 完整 metadata／風險 Gate 未通過；10/12 UI 表不能替代目前 read-back | 不顯示 Secret 值；不建立/刪除資源 |
| P2 | IN_PROGRESS | CY Web／CYACC 同正式 CYID 已確認；Invoice 仍連舊 CYID；見第 9 節新基準 | 精確 bundle 指紋及所有 Consumer／設定 owner 尚未全部驗收 | 先做不碰資料的 source PR，不據此切換 authority |
| P3 | BLOCKED | 等 Invoice D1 新 checkpoint，再核權威差異及角色／App Access／Workspace 接續 | CY Web／CYACC 已同 authority，不重做已完成切換；Invoice 另列否決／回復條件 | 不覆蓋 Identity D1，不移出 verifier／Session token |
| P4 | IN_PROGRESS | CYACC #475 已合併，CY Web 發布 guard／固定鎖已合併並完成首次 main dispatch 部署驗收；metadata 搬移受 P1 名稱／scope 缺口阻擋 | PR CI 與同目標 read-back；CYACC 合併／發布分離已生效；首次新模式發布待驗收 | 不先變更 Binding、不加強制 Reviewer 或遠端測試 Worker |
| P5 | BLOCKED on P3/P4 | 分 App 做 CYID 權威／App Access／Workspace 安全接續；特別保護 CYInvoice Device/Token/Offline/業務歷史 | 真實登入/Session/Role/Access/revoke、Device／Offline/reconnect 和資料連續性驗收；資料備份及回復證據 | 每次只切一個 Consumer，遇歧義停下，不自動重建 Workspace／Employee |
| P6 | BLOCKED on P5 | 完整退役不再使用的 Dev **部署入口**及多餘 Variables/Secrets／branch triggers（P4 可先停用已驗收的重複寫入） | 連續期間無活躍依賴、來源引用歸零、回復方案仍可執行 | 不在此階段刪實際 Worker/D1，也不重試 10339 |
| P7 | BLOCKED（另需明確資源清單與核准） | 如確有價值，評估歷史實體 Worker/D1 的個別退場 | 有 owner、可恢復快照、影響/成本證據及實際目標逐一核准 | 未達 Gate 永久保留，不盲刪 |

### 公開文件中的匿名資源表（只用 Alias，不放真實資源 ID）

| Alias | Owner | 已知保護原因 | 真實 read-back | 處置 |
| --- | --- | --- | --- | --- |
| CYID-P | CYapps | CYACC／CY Web 正式 Identity authority | active bindings 已確認；資料接續未全驗收 | PROTECT |
| CYID-D | CYapps | CYInvoice 仍依賴的 Identity authority | active bindings 已確認 | PROTECT UNTIL MIGRATION |
| CYACC-P | CYapps | 正式帳務 Worker/D1/Backup 與隔離復原 DB | 部分完成，見第 9 節 | PROTECT |
| CYWEB-EXISTING | chihyuan-web | 現存 Web/業務 D1/入口 | active bindings 已核；完整 owner／回復未完，見第 9 節 | PROTECT |
| CYINVOICE-EXISTING | CYapps | 原 Workspace／Device／Token Hash／Invoice History | active bindings 已核；資料連續性待新 checkpoint，見第 9 節 | PROTECT |
| PVT-CLOUD-UNKNOWN | CYapps_PVT | main 未見 deploy workflow，不等於不存在雲端 | 未完成 | UNKNOWN |
| HISTORICAL-PROBE | CYapps | 過往刪除失敗版本，禁止 retry | 歷史文檔已留存 | NO DELETE RETRY |

## 5. 每次續作必填交接格式

~~~text
日期／時區：
工作階段（P0 ~ P7）與狀態：
Repository、branch、main SHA、PR：
已讀取資料與來源（只含不敏感摘要）：
Cloudflare 資源（僅用 Alias）：
實際完成操作（read-only / PR / config / deploy / mutation）：
CI / runtime read-back／人工驗收：
CYACCweb 預期發布模式、目前是否生效及成功 deploy 證據：
是否建立遠端測試 Worker／Version（預期：否）：
CYID Authority／CYInvoice Workspace/Device/Data Continuity：
阻擋原因／未驗證事項：
Recovery 前置條件或參考（不含 Secret／ID）：
下一個 Gate／工作：
~~~

### 2026-10-12 15:36 (Asia/Taipei) — P1 唯讀連線再次受阻

- 使用者已明確授權在需要時透過安全的瀏覽器互動自行登入 Cloudflare 與 GitHub；但**本次沒有進入登入階段**。Browser Use 回報 `insufficient_credits`，要求補充 Browser Use 專案額度，重試無效。**這不是驗證失敗、也不是缺少使用者登入授權。**
- 上一輪無登入工作階段且瀏覽器導覽回報 `ERR_TUNNEL_CONNECTION_FAILED`。兩輪都**沒有讀到** Cloudflare Dashboard 的 Worker／D1 清單、Service Bindings／Metrics，也沒有讀到三個 Repo GitHub Settings 的 Environment、Secret/Variable 名稱或保護設定。
- Source-only 的 GitHub workflow/設定引用仍有效，但不能當作目前雲端實體資源數量或 Settings 實際設定的證據。**P1 繼續 BLOCKED ON LIVE READ ACCESS；P2～P7 不因此視為通過。**
- 可接續路徑：獲得可用的唯讀瀏覽器工作階段、專案瀏覽器額度，或其他**受核准且不輸出 Secret**的唯讀管理 API／清單後，再補實際資源的匿名摘要。不得為查詢方便新建遠端測試 Worker、變更 Cloudflare Token 權限、觸發 Production Deploy，或要求使用者把登入資訊貼在 Chat。
- 本次僅更新 AITeam 的跨 Repo 工作進度文件；CYACCweb `main` 自動部署與其他正式運作／GitHub Environment 完全未動。

### 2026-10-12 — 初始盤點與主控入口

- 已確認 AITeam/CYapps/CYapps_PVT/chihyuan-web 的 COMMON_RULES_VERSION 都是 2.8.0。AITeam 是共通規則母本，但**本文件不修改共通規則**，也不新增另一層規則。
- GitHub read-only 來源為三個管理 Repo 的 main workflow、Wrangler／部署程式、CYID／CYInvoice handoff；PVT 的 Private 內容不轉存到 AITeam Public repo。
- 尚未有 Cloudflare Dashboard 的即時 Worker/D1 全量清冊，且尚未有 GitHub Environment 實際名稱及 Secret/Variable name/scope 的直接 read-back。這部分以 P1 待辦明列，不得假裝完成。
- 使用者已明確要求：**CYACCweb main 自動部署保持不變**；不再製造任何遠端測試 Worker／probe；目標是單一正式雲端，不做粗暴刪除。
- 本次 AITeam 文件 PR 僅建立執行主控，不變更 CYID/CYInvoice/CYACC/CY Web runtime、Worker、D1、Service Binding、GitHub Secret／Variable、Deploy Workflow 或 Release。

## 6. Source 核對、整併草案與本地 MCP 交接（2026-10-12 歷史）

本節為歷史紀錄；執行前採第 9 節新基準，不重做已解決項目。

### 6.1 證據範圍與基準

本節是 **source-only 準備**，不是 Cloudflare／GitHub Settings 的實際清冊。P1 尚未完成，P2 的部署版本對照尚未通過，P3～P7 的 Gate 不變。此輪只更新 AITeam 文件；不修改產品 source、Workflow、Environment、Variables／Secrets 或遠端資源，不觸發 deploy／migration／Release。

| Repo | 此輪核對的 main commit | 使用範圍 |
| --- | --- | --- |
| AITeam | `d673a110814e9420522da0708f2e3d2b8589d74a` | 主控文件更新前基準 |
| CYapps | `c824310e2269777e8d5555d85475e975eed61897` | 四條 Cloudflare deploy Workflow 與相關 resolver／preflight |
| chihyuan-web | `395be0975f1ea09924ce0e03d30589b5f62a2db6` | development-deploy Workflow 與部署模板 |
| CYapps_PVT | `8ff622e90dad30143c09414c3b86feed1915902b` | Workflow 路徑清單；不複製 Private 程式內容 |

三個產品 Repo 的共通規則 blob 與 AITeam 已讀母本相同。晚間接手時若 main 已變更，先檢查本次相關檔案差異；本節不代表此後的新 source 已核對。

### 6.2 Workflow 引用與觸發對照

**Environment 欄是 YAML 宣告；Variables／Secrets 欄是引用名稱。它們不證明 Settings 中名稱存在、scope 正確、值正確或已授權。** `permissions: contents: read` 只描述這些 Workflow 的 GITHUB_TOKEN 權限，不能代替 Repository collaborator、GitHub App 或 Cloudflare Token 權限盤點。

| 用例 | Source 觸發條件 | 宣告 Environment | Variables 引用名稱 | Secrets 引用名稱 |
| --- | --- | --- | --- | --- |
| CYACCweb | main 相關路徑 push；workflow_dispatch；PR 只驗證 | development | `CF_BACKUP_TOPOLOGY`、`CF_CYACCOUNTINGWEB_CUSTOM_DOMAIN`、`CF_D1_DATABASE_ID`、`CF_D1_DATABASE_NAME`、`CF_R2_BACKUP_BUCKET`、`CF_WORKER_NAME` | `CLOUDFLARE_ACCOUNT_ID`、`CLOUDFLARE_API_TOKEN`、`CYID_WORKER_NAME` |
| CYID-D | 指定 deploy 分支 push；dispatch 預設 preflight／可選 upgrade；PR 只驗證 | development | `CYID_D1_DATABASE_ID` | `CLOUDFLARE_ACCOUNT_ID`、`CLOUDFLARE_API_TOKEN`、`CYID_WORKER_NAME` |
| CYID-P | workflow_dispatch；PR 只驗證 | development | `CYID_D1_DATABASE_ID`、`CYID_PRODUCTION_CORE_CONSUMER_WORKER_NAME` | `CLOUDFLARE_ACCOUNT_ID`、`CLOUDFLARE_API_TOKEN`、`CYID_WORKER_NAME` |
| CYINVOICE-EXISTING | 四條指定分支 push；dispatch 預設 preflight，可選其他三種會修改遠端的模式；PR 只驗證 | development | 無 vars.* 引用 | `CLOUDFLARE_ACCOUNT_ID`、`CLOUDFLARE_API_TOKEN`、`CYID_WORKER_NAME` |
| CYWEB-EXISTING | deploy/cyweb-development push 或 workflow_dispatch；PR 只驗證 | development | `CF_BACKUP_ENABLED`、`CF_BACKUP_R2_BUCKET`、`CF_BACKUP_SCHEDULE_ENABLED`、`CF_D1_DATABASE_ID`、`CF_D1_DATABASE_NAME`、`CF_IDENTITY_APPLICATION_ID`、`CF_IDENTITY_SERVICE`、`CF_IDENTITY_WORKSPACE_ID`、`CF_WORKER_NAME`、`GCS_BUCKET` | `CLOUDFLARE_ACCOUNT_ID`、`CLOUDFLARE_API_TOKEN`、`GCS_SERVICE_ACCOUNT_JSON` |
| CYapps_PVT | 目前只有 CYWatermark build 與治理／規則同步 Workflow，未見 Cloudflare deploy Workflow | 雲端部署未知 | 雲端部署引用未知 | 雲端部署引用未知 |

Source 證據（以下 Workflow／scripts 均在上述 main 基準核對）：

- CYACCweb：`.github/workflows/cyaccountingweb-deploy.yml`、`apps/CYAccountingWeb/scripts/resolve-cyid-production-runtime.mjs`。
- CYID：`.github/workflows/cycloudidentity-deploy.yml`、`.github/workflows/cycloudidentity-production-deploy.yml`、`apps/CYCloudIdentity/scripts/render-production-config.mjs`。
- CYInvoice：`.github/workflows/cyinvoice-cloud-dev-deploy.yml`、`apps/CYInvoice/cloud/scripts/deployment-preflight.mjs`。
- CY Web：`.github/workflows/development-deploy.yml`、`wrangler.deploy.template.jsonc`。
- CYapps_PVT：只記錄 Workflow 清單分類，未把 Private source、設定或 Key 轉存 AITeam。

### 6.3 已確認的 Source 行為與整理候選

| 觀察 | 已確認的行為 | 後續草案／前置 Gate |
| --- | --- | --- |
| CYACCweb 自動部署 | `main` push 且路徑為 `apps/CYAccountingWeb/**` 或自身 deploy Workflow 時，先 validate 再 deploy；PR 不 deploy。保留本地 D1 migration 驗證、bundle dry-run、正式 secure-login 與前端資產 smoke。 | P4 若整理 Environment 或輸入，保留上述觸發／驗證；不能添加 Required Reviewers，也不能改成僅手動部署。Settings 實際保護規則與最新部署成功狀態仍 UNKNOWN。 |
| 正式 CYID 名稱來源 | CYID Production 與 CYACC resolver 都由 `CYID_WORKER_NAME` 的 Dev 名稱推導正式目標；CYACC 再檢查 Provider 的 production 宣告與 CYACC scope。 | 實際 Binding 全量核對後，評估明確的正式 Provider 輸入；保留 production read-back gate，不直接把推導改成硬編碼。 |
| 多 App 同一 Environment | CYapps 的 CYID／CYACC／CYInvoice Workflow 都宣告 `development`；CY Web 也宣告同名環境，但屬另一 Repo，不能當作同一 scope。 | P1 先對照 repo／environment scope 與未引用名稱；不能只憑泛名存在就斷言 collision。P4 才確定是否採 per-App 前綴與正式 Environment。 |
| CYACC Identity scope owner | `CF_IDENTITY_SERVICE`、`CF_CYID_APPLICATION_ID`、`CF_CYID_WORKSPACE_ID` 由 Provider read-back 在 deployment job 注入，不是本 Workflow 的 GitHub vars 引用。 | 保持 Provider 作為 scope owner；不另複製一組 GitHub scope 真值。實際 ID 不進 Public 文件。 |
| CYInvoice 雙入口 | Source 定義 `IDENTITY` 與 `IDENTITY_AUTHORITY`，後者 entrypoint 為 `ConsumerAuthoritySync`，兩者應連同一 Provider。 | 晚間查 Active Version 的實際 service target／entrypoint；兩入口不能當成重複 Worker 而刪減。保留原業務 D1 與 Device／Workspace continuity。 |
| Private Repo 雲端需求 | 未見 Cloudflare deploy Workflow。 | 查實際清冊、手動管理資源及 Repo Settings；無證據前維持 UNKNOWN，不新建空環境。 |

建議執行順序仍是 **P1 實際清冊 → P2 deployed/source/Binding 對照 → P3 authority 差異 → P4 逐 Repo 輸入整理 → P5 個別 Consumer 接續**。Source 準備不授權 P4 修改，更不代替正式切換、Secret 重設或資料遷移的核准。

### 6.4 晚間本地 MCP 唯讀工作單

1. 讀 AITeam 最新 `main:docs/infrastructure/CLOUDFLARE_CONSOLIDATION_PLAN.md`，確認三 Repo main 與相關 source 是否更新。只使用使用者既有授權的本地 MCP；先確認 callable tools 與必要 scope，不新增 Token 權限。若 capability 不足，標 UNKNOWN。
2. **Cloudflare 清冊**：列出全部相關 Workers／Pages（若有）、active deployments 與 Version；檢查單一或多版本流量配置、Custom Domains／routes、D1 binding、R2、Cron。查 Requests 的時間區間與可用來源，無法查詢或無流量只能記錄事實，不能等同可刪。
3. **綁定清冊**：逐 Active Version 核對 binding name、type、service target 與 entrypoint；不只查最新上傳但未啟用的 Version。把 CYID-P、CYID-D、CYACC-P、CYWEB-EXISTING、CYINVOICE-EXISTING 與歷史 probe 分開分類；歷史 probe 不建立、不執行、不刪除重試。
4. **D1 metadata**：核對資料庫／Worker 關係、責任與現有 recovery 能力；P1 不查 Employee、Credential、Session、Workspace／Device 明細或業務資料，不匯出 D1。P3 所需的非敏聚合／fingerprint 另依 Gate 處理。
5. **三 Repo GitHub Settings**：Environment 名稱；repo／environment 的 Variable／Secret 名稱與 scope；Required Reviewers、wait timer、deployment branch／tag policy、可用 bypass 設定；main branch protection／rulesets；Actions allowed policy 與預設 Workflow permissions。Repository collaborator／App／Cloudflare Token 權限只查可見 metadata，不讀 token／key 值；不能取得則標 UNKNOWN，不因 workflow 的 `contents: read` 推斷管理權限。
6. **CYACC auto-deploy 專項**：確認 Workflow 啟用、main path trigger、Environment 沒有阻擋自動部署的 approval／branch rule；讀最近一筆相關 main deployment 的 run／job 結果、source SHA 與 active version 對照。只讀既有 run，不 dispatch 或 rerun；缺少證據不宣稱自動部署已通過 live 驗收。
7. **Settings 名稱比對**：對照 6.2 的引用，分類為「引用且存在／引用但缺失／存在但未被已核對 Workflow 引用／同名跨 scope 待釐清」。未引用只代表本次 source 範圍未引用，不能直接認定可刪；避免要求重建已刪除的 `CYID_WORKER_NAME` Variable。
8. 使用匿名摘要回寫 AITeam PR，列出 source 與 runtime 不一致、UNKNOWN、每個資源 owner／依賴／保護原因；達到完整清冊與保護風險 Gate 才更新 P1。P2 再對照全部 Consumer 與實際 deployed source，不先修改 Runtime。

MCP／API 回應在送入模型或可公開 log **之前**，就必須限制欄位：Secret 只保留 name／type／scope，不取 Secret 值；不輸出 Token／Cookie／密碼 verifier、Session、個資或業務資料。若 Worker settings API 同時傳回 plain_text 與 sensitive scope，於受保護本地流程裁切，不把原始完整 settings dump 貼進 Chat 或 Git。真實資源名稱／ID 的核對留在受保護本地 mapping；Public AITeam 只用 Alias、存在狀態、數量及不敏感比較結果。Private Repo 若有敏感名稱，公開摘要只保留匿名分類／數量。

公開摘要格式：

| Alias | Owner Repo | Active Version／source 一致性 | D1／R2／Cron | Service Binding 目標 Alias／entrypoint | Requests 區間／摘要 | 回復／保護原因 | UNKNOWN |
| --- | --- | --- | --- | --- | --- | --- | --- |
| （每個資源一列；不填真實名稱／UUID） | | | | | | | |

GitHub 公開摘要只列可公開的 Environment／Variable／Secret 名稱、scope 與保護狀態；Secret 名稱可見不代表值可讀。管理權限以角色／允許能力描述，不公開個人名單、Private Key、App credential 或內部 ID。

### 6.5 本輪交接

- 日期／時區：2026-10-12，Asia/Taipei；工作階段：P1 前置準備完成，P1 仍 BLOCKED ON LIVE READ ACCESS。
- AITeam P0 文件已由既有 PR #70／#71 合併 main；本輪追加 source 對照、候選方案與本地 MCP 工作單，不修改永久規則或產品版本。
- ChatGPT Work 內建 Cloud Browser 已正常開啟 Cloudflare 登入頁；Google OAuth 頁兩次顯示 `502 Bad Gateway / Connection refused`。尚無成功登入或 Cloudflare 清冊證據；本輪不用第三方 Browser Use，也不把其舊額度阻擋當成目前問題。
- 使用者決定晚間回家改用既有本地 MCP 連 Cloudflare。未讀取登入憑證或 Secret 值，未取得三 Repo Settings 實際名稱／權限清冊。
- 本輪操作類型：GitHub source 唯讀＋AITeam 文件 PR。CYACC main auto-deploy source 保持原樣；live 設定／最新成功 run 尚未驗證。未建立、上傳或刪除任何遠端 Worker／Version；未改 Identity authority 或 CYInvoice 資料。
- 下一個 Gate：依 6.4 取得受保護的實際清冊與匿名摘要，再更新 P1；不得把此輪文件準備誤作 P1/P2 完成。

## 7. 本地 MCP 實際盤點（2026-10-12 歷史）

Settings UI 名稱／數量與「存取缺口已解除」只適用當時本地登入工作階段。10/17 GitHub 名稱 API 仍為 403；不能推定 production scope 現在仍為空。第 7 個 D1 與 binding 變化見第 9 節。

### 7.1 P1 狀態與清冊範圍

Cloudflare 本地 MCP 已成功取得目前可見帳號的資源清冊及活躍版本 metadata。**P1 仍為 BLOCKED**：三 Repo GitHub Settings 的名稱／scope 與保護設定已取得；資源 owner 的精確對照、Private Repo／外部部署依賴、Cloudflare Token grants 及完整回復風險仍有 UNKNOWN。P2 至 P7 的 Gate 維持不變。

- 查詢時間：2026-10-12 21:34 至 21:48（Asia/Taipei）。Account 清單 1/1；Workers scripts 無 tag filter，回傳 5；D1 6/6；Custom Domain 2/2；Pages 0/0；可見 Zone 1/1、Worker routes 0。R2 bucket 清單回傳 2；API 未提供分頁欄位，因此範圍記為該次未篩選回應，不能推論其他帳號或 jurisdiction。
- Active deployment 採各 Worker deployment 清單中 `created_on` 最新一筆，逐一查其版本 binding。5 個 Worker 均為單一版本 100% 流量，沒有使用「最新上傳版本」替代活躍版本。
- CYapps main 已前進至 `daa12582990f729fd8f834826d492e58ed945802`；相對 6.1 基準新增一個 CYERPAutoInput commit，25 個 changed paths 未改動本次五條 Cloudflare workflow 或 CYID／CYInvoice／CYACC source。chihyuan-web、CYapps_PVT main 與 6.1 基準相同。
- 公開摘要只保存 Alias、binding／Secret 名稱、type、存在狀態、數量與非敏比較；Worker 真名、D1 UUID、Workspace／Employee ID、Secret 值、業務明細及 R2 object keys／內容沒有寫入本文件。

### 7.2 活躍資源與依賴

下列用例分類結合名稱線索、runtime APP_ENV 與既有 source 責任；它們仍不是 GitHub Settings 內真實部署目標與 owner 的完整驗收。Owner Repo 欄沿用既有 source 的主責 repo，資源精確 owner／外部依賴待補。

| 用例 Alias | Owner Repo | 活躍版本 metadata | D1／R2／Cron | Service Binding | Custom Domain | 保護與 UNKNOWN |
| --- | --- | --- | --- | --- | --- | --- |
| CYACC-P 候選 | CYapps | version number 219；100%；2026-10-12 11:09 啟用 | DB05；R2-01；`30 19 * * *` | `IDENTITY` → CYID-P 候選，default | 1 個 enabled | 保護正式帳務與自動部署；source SHA 精確對照、完整 recovery 待驗 |
| CYID-D 候選 | CYapps | version number 122；100%；APP_ENV=development；2026-10-12 12:20 啟用 | DB03；無 R2 binding／Cron | 無 service binding | 0 | CY Web／CYInvoice 目前依賴；不可先退場 |
| CYID-P 候選 | CYapps | version number 7；100%；APP_ENV=production；2026-09-30 18:39 啟用 | DB01；無 R2 binding／Cron | 無 service binding | 0 | CYACC 目前依賴；Named RPC 能力與跨環境權威差異待驗 |
| CYINVOICE-EXISTING 候選 | CYapps | version number 94；100%；APP_ENV=development；CYID_ENABLED=true；2026-10-12 13:05 啟用 | DB06；無 R2 binding／Cron | `IDENTITY` → CYID-D 候選，default；`IDENTITY_AUTHORITY` → 同一 Provider，ConsumerAuthoritySync | 0 | 保護原業務 D1、Workspace／Device／Token 與歷史；本輪未讀資料明細 |
| CYWEB-EXISTING 候選 | chihyuan-web | version number 50；100%；2026-10-12 10:59 啟用；SOURCE_VERSION=0.7.23 | DB04；無 R2 binding／Cron | `IDENTITY` → CYID-D 候選，default | 1 個 enabled | 業務服務仍活躍；backup 設定與部署輸入待核 |
| DB02 | UNKNOWN | 未出現在本次 5 個活躍版本 D1 bindings | 獨立 D1；名稱僅提示 Identity development 類別 | UNKNOWN | 不適用 | PROTECT；其他版本、外部／手動用途與 owner 未完成核對 |
| R2-02 | UNKNOWN | 未出現在本次 5 個活躍版本 R2 bindings | Bucket 名稱僅提示 CY Web 用途；object metadata count=0 | UNKNOWN | 無 custom domain；r2.dev disabled | PROTECT；無物件或活躍綁定不是可刪除證據 |
| PVT-CLOUD-UNKNOWN | CYapps_PVT | 目前 5 個 Worker 清冊未能精確歸屬此 repo | UNKNOWN | UNKNOWN | UNKNOWN | 無 deploy workflow 不證明沒有手動管理資源 |
| HISTORICAL-PROBE | UNKNOWN | 未從本次 Worker script 清單辨識獨立 probe；歷史版本不等於已清理 | UNKNOWN | UNKNOWN | UNKNOWN | 保留歷史限制；沒有建立、執行、上傳或刪除重試 |

Identity D1（DB01／DB03）與三個 App 業務 D1（DB04／DB05／DB06）仍分離。6 個 D1 的 metadata 與當前 Time Travel bookmark 均可讀；bookmark 可取得不等於完整備份、指定歷史時間可回復或 restore drill 已通過，本輪沒有 restore／export／資料表查詢。

兩套 CYID 的已上傳版本 metadata 已完整分頁核對：開發候選 122/122、正式候選 7/7，沒有 metadata 讀取失敗。DB02 未出現在這 129 個版本的 D1 binding 中；其他 Worker 歷史版本、dispatch namespace、外部使用及手動用途不在此項完成範圍。開發候選版本 118 至 122 的 metadata 可見 `ConsumerAuthoritySync`；正式候選 7 個版本均未見相同 export metadata。**未見 metadata 不能取代 deployed source 或實際 RPC contract 驗證**，因此 CYInvoice 不得依此直接切換 Provider。

### 7.3 Requests 與備份現況

Workers Observability log dataset 在 2026-10-11 21:35 至 2026-10-12 21:35（Asia/Taipei）回傳 0 logged events。另以 [Cloudflare GraphQL Workers Metrics](https://developers.cloudflare.com/analytics/graphql-api/tutorials/querying-workers-metrics/) 的 `workersInvocationsAdaptive` 在相同區間取得以下聚合；limit=100，回傳 5 個 scriptName groups，輸出前已轉為 Alias。這是 Adaptive Analytics 數字；errors 欄是該 dataset 的 Worker errors，不代表所有 HTTP／業務錯誤均為零。

| 用例 Alias | GraphQL requests | Worker errors | subrequests |
| --- | --- | --- | --- |
| CYACC-P 候選 | 357 | 0 | 357 |
| CYID-D 候選 | 415 | 0 | 1 |
| CYID-P 候選 | 247 | 0 | 0 |
| CYINVOICE-EXISTING 候選 | 913 | 0 | 329 |
| CYWEB-EXISTING 候選 | 73 | 0 | 66 |
| 合計 | 2005 | 0 | 753 |

5 個 Worker 的 script-level observability config 均回傳 null、tail consumers=0、logpush=false；這些 metadata 與 log 查詢結果不構成無流量證據。是否需要新增觀測設定屬後續變更，本輪沒有啟用 logging 或讀取請求內容。

- **CYACC／R2-01**：object metadata 完整單頁回傳 38 個物件、合計 4,244,005 bytes，最新 uploaded=2026-10-12 03:30:02（Asia/Taipei），與目前 Cron 時間相符。Lifecycle 有 enabled 的全 Bucket 45 日刪除規則，沒有 Bucket lock。這只證明物件與保留設定，不證明備份內容完整或可還原；GCS 相關 runtime Secret 名稱可見，不能證明實體 GCS Bucket、有效憑證或備份存在；GCS recovery UNKNOWN。
- **CY Web／R2-02**：object metadata 完整單頁回傳 0 個物件；同樣有 enabled 的全 Bucket 45 日刪除規則，沒有 Bucket lock。活躍版本無 `BACKUP_R2`、`BACKUP_ENABLED`、`GCS_BUCKET`、`GCS_SERVICE_ACCOUNT_JSON`，沒有 Cron。依目前 `worker/backup/runtime.ts` 的啟用條件，這組活躍 binding 不會建立 backup service；是否原本刻意停用與 GitHub `CF_BACKUP_ENABLED`／`CF_BACKUP_SCHEDULE_ENABLED` 的實際值仍待確認，不自行啟用、補 Secret 或改排程。

### 7.4 GitHub Settings 與 CYACC 自動部署

2026-10-12 22:13 至 22:22（Asia/Taipei），使用使用者已登入的本地瀏覽器唯讀核對三 Repo Environment、repo／environment 的 Secret／Variable 名稱與 scope、部署保護、Actions、Branches／Rulesets、Collaborators 及 GitHub App grants。設定名稱從表格第一欄裁切取得，不輸出 Variable 值；沒有開啟 Secret 編輯、讀取 Secret 值或變更任何設定。本機 GitHub CLI 既有登入無效不影響此次 UI 讀取。

| Repo／scope | Environment 名稱 | Secret／Variable 數量 | 部署保護 |
| --- | --- | --- | --- |
| CYapps／Environment | development | 6／16 | Required reviewers 未啟用；wait timer 未啟用；No restriction；允許 admin bypass |
| CYapps／Environment | production | 0／0 | 同上；目前為空 Environment，不視為可直接搬入部署 |
| CYapps／Environment | cyaccountingweb-cyid-development | 0／0 | 同上；空設定不證明歷史部署入口可刪 |
| CYapps／Repository | 不適用 | 0／0 | 沒有 repo 層級 Secret／Variable |
| chihyuan-web／Environment | development | 2／6 | Required reviewers 未啟用；wait timer 未啟用；No restriction；允許 admin bypass |
| chihyuan-web／Repository | 不適用 | 0／0 | 沒有 repo 層級 Secret／Variable |
| CYapps_PVT／Environment | 無 Environment | 0／0 | 無對應 deployment protection |
| CYapps_PVT／Repository | 不適用 | 0／0 | 沒有 repo 層級 Secret／Variable；不代表沒有手動雲端資源 |

可公開的 Settings 名稱如下；同名跨 Repo 屬不同 scope，不因名稱相同就合併。此次沒有觀察到 repo／environment 同名碰撞。

- **CYapps／development Secrets（6）**：`CLOUDFLARE_ACCOUNT_ID`、`CLOUDFLARE_API_TOKEN`、`CYID_BOOTSTRAP_SECRET`、`CYID_BREVO_API_KEY`、`CYID_OTP_PEPPER`、`CYID_WORKER_NAME`。
- **CYapps／development Variables（16）**：`CF_BACKUP_TOPOLOGY`、`CF_CYACCOUNTINGWEB_CUSTOM_DOMAIN`、`CF_D1_DATABASE_ID`、`CF_D1_DATABASE_NAME`、`CF_IDENTITY_SERVICE`、`CF_R2_BACKUP_BUCKET`、`CF_WORKER_NAME`、`CYID_ACCOUNT_PORTAL_URL`、`CYID_CORE_ACCOUNT_APPLICATION_ID`、`CYID_D1_DATABASE_ID`、`CYID_D1_DATABASE_NAME`、`CYID_EMAIL_DAILY_BUDGET`、`CYID_EMAIL_FROM`、`CYID_EMAIL_PROVIDER`、`CYID_LOGIN_RATE_LIMIT_NAMESPACE_ID`、`CYID_SESSION_TTL_SECONDS`。
- **chihyuan-web／development Secrets（2）**：`CLOUDFLARE_ACCOUNT_ID`、`CLOUDFLARE_API_TOKEN`。
- **chihyuan-web／development Variables（6）**：`CF_D1_DATABASE_ID`、`CF_D1_DATABASE_NAME`、`CF_IDENTITY_APPLICATION_ID`、`CF_IDENTITY_SERVICE`、`CF_IDENTITY_WORKSPACE_ID`、`CF_WORKER_NAME`。

#### Workflow 引用名稱比對

只比對 6.2 的四條 CYapps Cloudflare workflow 與一條 chihyuan-web workflow；「未直接引用」不代表整 Repo／腳本／手動管理不用該名稱。

| 用例 | 引用且存在 | 引用但 Settings 缺失 | 處置與限制 |
| --- | --- | --- | --- |
| CYapps／CYACC、CYID-D、CYInvoice | 所有已列 GitHub Secret 引用及這三條 workflow 的 Variable 引用均有對應 development 名稱 | 無 | 名稱存在不證明值正確；保持 CYACC main 自動部署 |
| CYapps／CYID-P | `CYID_D1_DATABASE_ID` 與三個 Cloudflare／CYID Secret 引用存在 | `CYID_PRODUCTION_CORE_CONSUMER_WORKER_NAME` | workflow 將其映射為 `PROD_CORE_CONSUMER_WORKER_NAME`，並在遠端讀取前要求非空；依當前 scope 清冊，啟動會在 prerequisite gate 失敗，不 dispatch 驗證、不猜值補建 |
| chihyuan-web／CY Web | 六個基本部署 Variable 與兩個 Cloudflare Secret 名稱存在 | Variables：`CF_BACKUP_ENABLED`、`CF_BACKUP_SCHEDULE_ENABLED`、`CF_BACKUP_R2_BUCKET`、`GCS_BUCKET`；Secret：`GCS_SERVICE_ACCOUNT_JSON` | 備份相關缺項；render 只有 `CF_BACKUP_ENABLED=true` 才要求備份配置，不能將缺項直接等同基本部署失敗；符合 7.3 活躍 backup 未啟用的線索 |
| CYapps／development | 另有 9 個 Variable、3 個 Secret 未被上述四條 workflow 直接引用 | 不適用 | Variable：`CF_IDENTITY_SERVICE`、`CYID_ACCOUNT_PORTAL_URL`、`CYID_CORE_ACCOUNT_APPLICATION_ID`、`CYID_D1_DATABASE_NAME`、`CYID_EMAIL_DAILY_BUDGET`、`CYID_EMAIL_FROM`、`CYID_EMAIL_PROVIDER`、`CYID_LOGIN_RATE_LIMIT_NAMESPACE_ID`、`CYID_SESSION_TTL_SECONDS`；Secret：三個 `CYID_BOOTSTRAP_SECRET`／`CYID_BREVO_API_KEY`／`CYID_OTP_PEPPER`；保留，待更廣引用／owner 核對 |

6.2 的 CYID-P Variable 清單已修正：`CORE_ACCOUNT_APPLICATION_ID` 出現在 `config.vars.CORE_ACCOUNT_APPLICATION_ID`，是生成的 Cloudflare runtime config 欄位，**不是 ${{ vars.CORE_ACCOUNT_APPLICATION_ID }} 的 GitHub Variable 引用**。不存在不構成需要補建的缺項。Settings 中 `CYID_WORKER_NAME` 只有 Secret，沒有同名 Variable；保留先前已完成的清理，不要求重建。

#### Branches Actions 與管理權限

- 三 Repo Branches UI 均為「Classic branch protections have not been configured」；Rulesets UI 均為「You haven't created any rulesets」。CYapps_PVT 另顯示 Private Repo rulesets 在升級前不會 enforce，與先前 API 403 feature eligibility 證據相符；本輪沒有升級 plan、改 visibility 或新建規則。
- 三 Repo Actions 均允許 all actions and reusable workflows，沒有要求 full-length SHA pinning；預設 Workflow permission 為 read repository contents and packages；`Allow GitHub Actions to create and approve pull requests` 未勾選。Public 兩 Repo 要求 first-time contributors approval；Private Repo fork workflows 未啟用、共享 actions／reusable workflow 為 Not accessible。這些 Settings 不取代每條 workflow 的顯式 `permissions:`。
- 登入 actor 為 repo owner；三 Repo Collaborators UI 均顯示沒有邀請 collaborator。只記角色與數量，不保存個人名單。
- **ChatGPT Codex Connector**：三 Repo 均已安裝；existing scope 為 All repositories；read checks／commit statuses／metadata，read-write actions／code／issues／PR／workflows。可見 grants 沒有 administration 或 secrets，與 connector 的 Settings API 讀取缺口相符。
- **Claude**：三 Repo 均已安裝；existing scope 為 All repositories；read administration／commit statuses／merge queues／metadata，read-write actions／checks／code／discussions／issues／PR／repository hooks／workflows。
- **Cloudflare Workers and Pages GitHub App**：只在 CYapps 已安裝；existing scope 為 selected repositories，清單只有 CYapps；read metadata，read-write administration／checks／code／deployments／PR。此 GitHub App grants 不是 Cloudflare MCP 或 workflow API Token 的 grants，不能互相代替；後兩者仍為 UNKNOWN。
- 以上只讀 existing grants，沒有擴張、撤銷或切換任何權限。

CYACC 最新已讀 main push run 為 [38198568768](https://github.com/simonliu1118-byte/CYapps/actions/runs/38198568768)，source SHA=`1f10193a7f09a8646ec8cf1300df12fdc05e1f20`，2026-10-12 11:08:17（Asia/Taipei）啟動；validate／deploy 與 production secure login entry、semantic frontend assets steps 均 success。Deploy step 的 11:08:56 至 11:09:03 區間包含目前活躍 deployment 的 11:09:00 建立時間，仍只有時間對應，source hash／artifact 與活躍版本精確比對待補。Workflow UI 提供 Disable workflow（已啟用）與 Run workflow 入口；source 與 development Settings 均保留 main 自動部署且未見 approval／branch 限制阻擋。本次只讀 UI 與既有 run，沒有 disable／dispatch／rerun。

### 7.5 下一步與交接

GitHub Settings 名稱／scope／保護規則的存取缺口已解除。下一步補 DB02、R2-02、Private／外部部署 owner、真實部署目標的非敏比對與 recovery 風險，才能通過 P1。已確認的 CYID-P prerequisite 缺項、空 Environment、backup 停用線索與 main 未設保護，納入後續 PR 計畫；不因盤點完成就自行填值、搬動 Environment、啟用備份或新增阻擋 CYACC 的 reviewers。

P1 Gate 通過後，P2 核對 CYID Named RPC 能力與 deployed source；P4 整理明確正式部署目標與 per-App inputs。低流量、空 Bucket、名稱含 dev 或本次未見引用，均不得升格為資源刪除依據。

~~~text
日期／時區：2026-10-12 晚間／Asia/Taipei
工作階段（P0 ~ P7）與狀態：P1 Cloudflare 與三 Repo Settings 實際清冊已取得；BLOCKED，owner／外部依賴／recovery 尚未通過 Gate
Repository、branch、main SHA、PR：AITeam；cloudflare/docs-local-mcp-p1-inventory-20261012；基準 b24d8c22c445ee54dc1b532dfd93292764316eba；PR 73
已讀取資料與來源：本地 Cloudflare MCP metadata／GraphQL 聚合；GitHub 已登入 UI 的名稱／scope／保護與 App grants；五條 workflow source／既有 Actions job summaries；無秘密值
Cloudflare 資源：7.2 Alias 與依賴；5 Workers、6 D1、2 R2、2 Custom Domains；Pages 0
實際完成操作：Cloudflare read-only GET／GraphQL query；GitHub Settings read-only UI；更新同一 AITeam 主控文件 PR
CI／runtime read-back：CYACC 既有 run success；文件 PR 最新 commit CI 以實際 checks 結果為準
CYACCweb main 自動部署是否仍正常：source／Settings 沒有阻擋；已讀 main push run、deploy／smoke success；精確 active source 對照待補；未變更
是否建立遠端測試 Worker／Version：否；未執行 probe、刪除 retry 或遠端 migration
CYID Authority／CYInvoice Workspace/Device/Data Continuity：只核對 Service Binding 目標／entrypoint 與 CYID_ENABLED；未讀資料明細，不宣稱 continuity 全部驗收
阻擋原因／未驗證事項：完整 owner／Private 外部依賴、Cloudflare Token grants、deployed source、指定歷史 recovery／restore drill
Recovery 前置條件：各 D1 可讀當前 bookmark；R2 metadata／lifecycle 已核；內容驗證、歷史回復時間與 GCS recovery UNKNOWN
下一個 Gate／工作：補 P1 關鍵缺口，未通過前不推進 P2 至 P7 的變更
~~~

## 8. 一次性檢討與修改收斂方案（2026-10-12 歷史，已被取代）

**本節修改方向與執行批次已由第 9 節取代，不直接照此執行。**「保留 CYACC main auto」、Invoice enabled 模式待修、CY Web 尚連舊 authority、CYID-P 前置設定缺失等均是當時假設；不可重做或以此阻擋新版方案。當前修改清單、部署入口、Settings 過渡及批次依第 9 節。

本次收斂採一套正式雲端、每個 App 一個正式部署目標與一條部署入口。開發使用 Local Wrangler／D1 與 CI；CYACC main 自動部署保留。以下是修改與驗收方案，尚未執行產品修改、Settings 搬移、身分切換或資源退場；第 4 節 P1～P7 Gate 繼續適用。

### 8.1 問題與優先順序

| 優先 | 問題與證據 | 修改方向 |
| --- | --- | --- |
| 第一批 | CYInvoice 除 Actions 外，W04 有一條未標 deleted 的 Workers Builds trigger，連到 CYapps 的 `cyinvoice/cloud-foundation-d1`，root 為 `apps/CYInvoice/cloud`，paths 為 `*`。部署命令使用 `npm run deploy:with-migrations`。70 筆 build 為 68 success／1 skipped／1 fail；最新建立於 2026-09-29 13:39:13（Asia/Taipei）。該成功 commit 的 package script 執行 remote migration 再 Wrangler deploy。 | 將 GitHub Actions 定為唯一例行入口；修好並驗收它後停用這條舊 Builds trigger。在此之前凍結舊分支的部署用途，避免另一條路徑覆蓋現有 CYID 設定。Build success 不代表目前 active version 仍由它產生。 |
| 第一批 | CYInvoice active `CYID_ENABLED=true`；普通 deploy 的 preflight 仍要求未啟用 CYID，版本上傳亦以未啟用模式檢查。 | 普通更新必須保留當前 authority 模式、兩個 Service Binding、原 D1／Workspace／Device。移除「一般部署必須關閉 CYID」的假設，不用關掉身分服務繞過檢查。 |
| 第一批 | CYID-P 缺 `CYID_PRODUCTION_CORE_CONSUMER_WORKER_NAME`；production renderer 要求 core CY Web 已連正式 Provider，CY Web 現在卻連 W02。 | 將「相容的 Provider 程式升級」與「Consumer 切換後的退場條件」分開，解開部署順序阻點；保留 production、D1、Contract 與 core application 檢查。 |
| 第一批 | CYID-P 與 CYACC resolver 都靠 Dev Worker 名稱推導正式名稱。CYapps production Environment 空白，實際部署仍讀 development。 | 改用明確正式 Provider 目標；遷移 GitHub Environment 時保留既有實體 Worker／D1。 |
| 第一批 | CYACC concurrency group 包含 event_name／ref；push 與 dispatch 可分屬不同 group，`cancel-in-progress=true`。 | 同 App、同正式目標用固定 deploy group，序列執行且不取消進行中的部署；驗證 job 可取消舊執行。遠端變更前排除過期 commit。 |
| 第二批 | CYID、CYInvoice、CY Web 的舊部署分支可 push 觸發遠端修改，與 main 審核流程分離。 | PR 做驗證；正式部署只取 main 的確定 SHA。CYID／CYInvoice／CY Web 保持明確 dispatch；CYACC 保留相關路徑 main push 自動發布。 |
| 第二批 | CY Web active 無備份 R2／cron，也沒有備份啟用與 GCS runtime names；現有程式設計需要 R2＋GCS。CYACC 有實際備份，restore 尚未驗收。 | 補齊逐 App 備份 owner、設定與隔離回復驗收。保留既有雙供應商設計，不把環境收斂順便改成單一備份供應商。 |
| 第二批 | main 無 branch protection／rulesets；三 Repo collaborator 為 0；Private rulesets 有方案限制。 | Public Repo 先設 PR 與必要 CI 的流程；不增加 sole maintainer 無法完成的必要他人審核，也不把 environment reviewers 加到 CYACC。Private 不以改公開／升級付費作預設解法。 |
| 第三批 | DB02、R2-02 完整 owner／外部依賴及歷史 recovery 尚不清楚。 | 完成 P1 保護清冊；暫留實體資源。空 bucket、低流量、名稱含 dev 都不足以判斷可刪。 |

本輪額外唯讀核對五個 Worker 的 Builds triggers 與 deploy hooks：W04 有上述 1 條 trigger；其餘四個 Worker 為 0；五個 Worker hooks 均為 0。未讀 build logs／environment variable values，也未修改 trigger。Cloudflare GitHub App 的安裝本身不代表每個 Worker 都啟用 Builds。

### 8.2 最終部署拓撲

| 系統 | 正式運行目標 | 開發方式 | 發布入口 |
| --- | --- | --- | --- |
| CYID | 一個 Identity Worker＋一個 Identity D1；既有 CYID-P 優先候選，依 P3 權威差異與回復方案定案 | Local D1、合成帳號、Consumer Contract CI | 一條 main-only dispatch workflow，預設 preflight，明確選 deploy 才改遠端 |
| CYACC | 保留 W01、DB05、現有域名／備份，連最終 CYID | Local／CI | 現有 main 相關路徑 push → Validate → Deploy → login／assets smoke；保留 main-only dispatch |
| CYInvoice | 保留 W04、DB06、原 Device／Token／Workspace／歷史；雙 Identity 入口連同一最終 CYID | Local／CI、既有 Windows 裝置驗收 | 一條 main-only dispatch；普通 deploy 保留已啟用 CYID，不再由多條 activation 分支改遠端 |
| CY Web | 保留 W05、DB04、域名；core application／Workspace 保持可核對 | Local／CI | 一條 main-only dispatch；不自行新增 main push 自動部署 |
| CYapps_PVT／其他無雲端 App | 有實際需求才配置雲端 | 既有本地與 CI build | 保持原產品流程，不建立空的 Cloudflare Environment／Worker／D1 |

目標是四個運行角色各有明確責任，其中三個 App 的業務 D1 彼此獨立。這不是立即把目前 5 Worker／6 D1 刪到 4／4；舊 authority、DB02 與歷史資源仍受 P7 保護。實體名稱含 dev 可先繼續使用，正式意義以部署設定、owner 與 runtime read-back 表達，不以改名重建服務。

Cloudflare 的邏輯環境收斂不影響 CYInvoice **AMEGO 測試／正式交易環境**的業務安全區分。

### 8.3 設定來源與 GitHub Environment

CYapps 與 chihyuan-web 各使用本 Repo 的 `production` GitHub Environment；不保留日常長駐 cloud development 成對部署。兩 Repo 的同名 Environment 是不同 scope。CYACC 所在環境保持沒有 required reviewers／wait timer，部署分支限制只允許 main。敏感憑證仍放 Secrets，Cloudflare 已有 runtime Secrets 以存在／類型檢查及保留為主。

| 類別 | 唯一 owner 與預定名稱 | 過渡方法 |
| --- | --- | --- |
| 共用部署憑證 | 每 Repo production 的 `CLOUDFLARE_API_TOKEN`、`CLOUDFLARE_ACCOUNT_ID` Secrets | 受保護設定流程承接已有值，不讀出或貼入 Chat；不因整理名稱輪替 Token。權限只在查明需求後另作評估。 |
| CYID 正式目標 | `CYID_PROVIDER_WORKER_NAME` 部署 metadata，CYID 的 D1 由 active binding 嚴格比對；`CORE_ACCOUNT_APPLICATION_ID` 仍是 Provider runtime config | 不靠 Dev 字尾推導，不新增 GitHub `CORE_ACCOUNT_APPLICATION_ID` 假真值。新名稱是提案，不是目前已存在的設定。 |
| CYACC metadata | `CYACC_WORKER_NAME`、`CYACC_D1_DATABASE_ID`、`CYACC_D1_DATABASE_NAME`、`CYACC_R2_BACKUP_BUCKET`、`CYACC_BACKUP_TOPOLOGY`、`CYACC_CUSTOM_DOMAIN` | 先將舊 `CF_*` 值映射到相同現有目標，完整核對後才讓 workflow 改讀。App／Workspace scope 繼續從 Provider read-back 取得。 |
| CYInvoice metadata | `CYINVOICE_WORKER_NAME`；業務 D1、Workspace、Device／Token 以原 active resource／業務 authority 為基準 | 不複製 Device Token 或 Employee 身分成 GitHub 真值；保留 exact-active-version inheritance 與 fingerprint gate。 |
| CY Web metadata | 本 Repo `CYWEB_WORKER_NAME`、`CYWEB_D1_DATABASE_ID`／`NAME`、`CYID_PROVIDER_WORKER_NAME`；scope 過渡保留既有 App／Workspace 設定並與 Provider 核對 | 模板改讀時逐一映射既有值，無法證明一致即停止。備份 gate 期間可受控配置、建立備份與隔離 restore；驗證成功後啟用例行排程。 |
| Runtime Binding | `DB`、`IDENTITY`、`IDENTITY_AUTHORITY`、`BACKUP_R2` 保持現有介面責任 | 名稱整理不改 App runtime 介面；未知 binding 不靜默丟棄。 |

`CYID_WORKER_NAME` 舊 Secret 在所有 source 引用與回復需求歸零前保留，不重建同名 Variable。未直接出現在 workflow 的 CYID Email／TTL／rate-limit 等 Settings，先追完整 script／runtime 引用再決定，不批次刪除。

**CYACC 的過渡順序必須是先備妥新 scope／新名稱，確認仍對同一 Worker／D1，再合併修改 workflow 的 PR。** 因為合併會自動部署，不能先合併讀取空 production Environment 的程式再補設定。舊 metadata／Environment 要等新路徑成功 deploy、read-back 與 smoke 後才退役；回復工作單不能只寫「revert PR」，還要列明它需要的舊 scope。

### 8.4 逐 Repo 修改清單

| Repo 與檔案 | 修改內容 | 必要驗證 |
| --- | --- | --- |
| CYapps：`cycloudidentity-deploy.yml`、`cycloudidentity-production-deploy.yml`；CYID `render-production-config.mjs`、`deployment-preflight.mjs` | 收斂為一條正式 workflow；明確 Provider 目標；切分 Provider 相容升級 gate 與 Consumer 已切換／舊 authority 退場 gate。保留 Secret、rate limit、authority fingerprint 與 D1 檢查。 | 缺 metadata／錯 D1／非 production／Contract 不符均在遠端修改前失敗；合成測試證明 upgrade 不必靠先切 Consumer 才可執行。 |
| CYapps：`cyaccountingweb-deploy.yml`；CYACC `resolve-cyid-production-runtime.mjs`、`render-wrangler.mjs` | production scope／CYACC 前綴，resolver 改讀明確 Provider；固定正式 deploy concurrency group、deploy cancel=false、dispatch main-only；保留 backup topology 與 main auto。 | PR 驗證不 deploy；新 metadata 與舊目標相同；main 自動發布＋正式登入／前端 smoke 成功。 |
| CYapps：`cyinvoice-cloud-dev-deploy.yml`；CYInvoice `cloud/scripts/deployment-preflight.mjs`、`cloud/package.json` 與部署文件 | 普通 deploy 支援當前 CYID enabled 狀態；保留雙入口、exact-active-version inheritance、TTL checkpoint、業務 D1；把一次性 prepare／activate 從常規發布退出；清楚區分 local scripts 與唯一正式入口。 | CYID enabled=true 與過渡狀態的 synthetic 測試；未知 binding／authority 變動／過期 checkpoint 失敗；現有 Device／Offline／重連 continuity 驗收。 |
| CYapps：Cloudflare W04 Builds 設定 | P4 同目標 Actions 路徑驗收後停用重複寫入 trigger，保留設定與回復記錄；P6 才完整退役／清理舊入口。不先刪 GitHub App 或升降 Token 權限。 | trigger read-back 無例行第二寫入者；branch push 不再產生 remote migration／deploy。 |
| chihyuan-web：`development-deploy.yml`、`scripts/render-deploy-config.mjs`、`wrangler.deploy.template.jsonc`、備份設定／文件 | production scope／明確正式輸入；main-only dispatch／固定 deploy group；保留 source SHA、Contract sync 與健康檢查；調整輸出避免 public log 回顯真實部署 metadata。配置既有 R2／GCS 備份須先確認 owner。 | dry-run／Contract CI；域名與 DB 不變；登入、core account access、revoke；隔離空目標的 backup restore 可驗證。 |
| Public Repo Settings／治理文件 | PR＋持續回報的 required CI；必要 Actions 權限；更新一次性部署／handoff 說明，退休舊分支觸發。 | 不新增 CYACC 部署人工核准；不將可能由 paths／branch filter 整條跳過的 workflow 直接設 required。 |
| CYapps_PVT | 補手動／外部部署 owner 核對；維持 Private 邊界 | 無雲端需求則無 Cloudflare 修改；Private 原碼／設定不進 Public 文件。 |

Workflow 檔名可在對應批次改為正式名稱，改名前先追跨檔引用。不建立通用部署框架或把各 App 的安全檢查塞入一個大型 wrapper；共用的是簡單的發布步驟與可追溯證據。

正式部署固定為：Local／CI → main SHA → protected metadata 與當前 runtime 比對 → 必要且已驗證的 migration → 同目標序列部署 → active version／binding read-back → App smoke。Deploy 開始前確認 SHA 仍符合發布策略，過期執行停止並改用新 main；手動與自動入口共用同一目標鎖。GitHub concurrency 不能約束 Cloudflare Builds 或人工 Dashboard 操作，因此唯一例行寫入者還要靠入口收斂。

### 8.5 身分接續與備份回復

1. 比對兩個 CYID 的非敏 authority 聚合、Application Registry／角色／App Access 與 Workspace mapping。以資料權威及可回復性決定正式目標，CYID-P 只是優先候選。不得把 Dev 的 Credential／Session 直接覆蓋正式 D1。
2. 正式 Provider 先具備當前所有 Consumer 所需 Contract／Named RPC 的相容程式。W03 metadata 未列 `ConsumerAuthoritySync` 是待查能力差異，不等於已證明 runtime 一定沒有此能力；先核 deployed source，再用本地相容測試及正式發布後的既有流程驗收，不建立遠端 probe。
3. 若選現有 CYID-P，CYACC 留原 authority；先接續 core CY Web，再逐步接續 CYInvoice。後者兩個入口在同一個已審查部署中同時指向正式 Provider。若 P3 決定別的目標，CYACC 另列專門切換步驟，不能沿用「無身分切換」批次。
4. 每個 Consumer 切換前列出帳號映射、角色／App Access、Workspace、既有登入的處理方法與回復相容性。帳號權威衝突、core access 變成不可用、資料 fingerprint 不符、回復未證明或 Device continuity 失敗都停止；不自動建立替代 Employee／Workspace。
5. 備份先保留 CYACC 的現行 R2／GCS topology、cron 與保存設定。CY Web 按現有 R2＋GCS 設計補齊條件輸入，優先核對 R2-02 是否可沿用；不為命名收斂新建備份 Worker／D1。回復用可驗證且隔離的空目標，符合既有 recovery 程式條件；驗證範圍、備份一致性及 GCS 可用性須寫入證據。

Worker 程式回退與 D1／R2 資料回復是兩種操作。Cloudflare 明確說明 Worker rollback 不回復所連接儲存的資料狀態；有 migration 或寫入時，先判斷舊程式與現有 schema／資料是否相容，不能以 rollback 成功宣稱帳號或業務已回復。[Cloudflare Worker rollbacks](https://developers.cloudflare.com/workers/versions-and-deployments/rollbacks/)

### 8.6 執行批次與完成條件

| 批次 | 執行與依賴 | 完成條件 |
| --- | --- | --- |
| R0：基準與 recovery | 補 P1 owner／外部依賴、P2 source／active version 精確對照；保護舊 Builds 分支。受影響 Identity／業務 D1 的可用備份、隔離 restore、schema 相容與回復證據，必須在 migration／authority 切換前通過。可先準備 source PR／本地驗證，未過 gate 不合併會部署的變更。 | 每個目標有 owner、設定來源、active 指紋、D1／Secret 保留與回復工作單；UNKNOWN 不被當作 unused。 |
| R1：修好唯一部署路徑 | 按 App 提 source PR：CYID 順序、Invoice enabled 模式、CYACC 明確輸入／concurrency、CY Web formal workflow。實作與獨立 review 分工，遵守各 Repo PR／CI／版本規則。 | Local／CI 與 source review 通過；原目標映射明確；沒有新增 remote test 資源。 |
| R2：Settings 與同目標發布 | P4 gate；預先備妥 production metadata／Secrets，驗證後逐 App 合併並發布。CYACC 合併觸發 auto；其他 App main dispatch。Invoice Actions 驗收後停用重複 Builds 寫入，保留舊設定／回復記錄，完整清理留 P6。 | 相同既有 Worker／業務 D1／域名；部署 read-back 與 smoke；無第二例行部署入口。這批不改 Consumer authority。 |
| R3：正式 authority 與 Consumer 接續 | P3／P4／P5 gate 及 R0 受影響資料的備份／隔離 restore／schema 回復證據；相容 Provider 升級，再 CY Web、CYInvoice，每次單一 Consumer；必要資料映射另列精確方案與核准。 | 所有 Consumer 同一正式 authority，角色／revoke／core access、Device／Offline／業務連續性及回復證據通過。 |
| R4：持續備份、治理、來源清理 | 完成例行備份排程與 Public main checks 流程；P6 退休舊 deploy branches／Environment／metadata，只刪確認無引用且無回復需要的設定。 | CYACC main auto 持續成功；CY Web 例行備份成功、沿用已驗收的隔離 restore 證據；source／Settings 清單一對一；main 必要 checks 無常態 Pending。 |
| R5：可選實體退場 | P7，獨立於環境收斂的完成。 | 每一 Worker／D1／bucket 另有 owner、可恢復證據、依賴為零與具體核准；未通過則保留。 |

治理檢查必須每個 PR 都回報；路徑篩選的 App CI 若要成為 required check，先設計永遠回報結果的入口。GitHub 指出整條 workflow 因 path／branch filter 被跳過時，其 required check 會留 Pending，因此不能直接把所有現有 path-filtered workflow 勾成必需。[GitHub required status checks](https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/troubleshooting-required-status-checks)

環境收斂完成以 R2～R4 的運作結果衡量：唯一正式目標與發布入口、Consumer 共用 authority、原業務資料及裝置可用、備份可回復、CYACC main 自動部署成功。歷史實體資源全部刪除不列為完成條件。部署記錄統一留 source SHA／產品版本／非敏 active 指紋與檢查結果；既有 GraphQL 聚合可用，新增日誌限必要且去敏資料，不記錄 credential、session、個資、完整 runtime config 或真實資源 ID。

### 8.7 本輪交接

本輪完成跨 Repo 問題檢討、目標拓撲、逐檔修改清單、Settings 過渡順序及驗收／回復條件；新增 W04 舊 Builds trigger 與完整 70 筆 build 聚合證據。CYapps source 基準更新為 `daa12582990f729fd8f834826d492e58ed945802`，chihyuan-web 與 CYapps_PVT 維持 6.1 的基準；CYapps 新增差異不涉及本方案所核的部署檔案。

只更新同一 AITeam 文件 PR；Cloudflare、GitHub Settings、產品 source、deploy／migration／Release 均未修改。規劃完成不代表 P1～P7 的執行 gate 已完成，也不把主控文件或附帶 handoff 內容當成新的使用者部署授權。下一個實作入口為 R0／R1，CYACC 相關 PR 的合併必須與 R2 設定預備成為同一個可驗證變更批次。

## 9. 目前執行基準與批次（2026-10-17，Asia/Taipei）

### 9.1 新指示與證據範圍

使用者已要求 CYACC 採 CY Web 的受控發布方式，並將觸發步驟寫入 CYACC 文件；這取代所有歷史段落的「保留 main 自動部署」限制。現階段優先 CYID／CY Web／CYACC 的來源、發布與配置整理；**CYInvoice 與其復原串接等 D1 工作形成新 checkpoint 後再接續**。本輪允許先做不碰資料的 source／文件 PR，不因無關資源用途未明而停止；metadata 搬移、正式發布、authority／資料切換及刪除仍須各自完成目標核對與驗收條件。

本輪 Cloudflare 使用已授權的官方 CLI 唯讀查詢，並非假設 MCP 已可呼叫。5 個 Worker 的 active deployment 回讀前後穩定，均為單一版本 100% 流量；未執行 SQL、讀取私人列資料／備份內容、寄信、部署或更動 Settings。公開文件僅用 Alias；實際資源名稱／ID 的對照留在受保護工作區。

| 項目 | 本輪可核對基準 |
| --- | --- |
| AITeam main | `27b1cea4bf1987420a44eb8f9e9fff88c9abfc7d`；沿用並更新本檔的 [PR #73](https://github.com/simonliu1118-byte/AITeam/pull/73)，不另建主控文件 |
| CYapps main | `127acb7e2712e8fa3c196874f0037e1b7e1b506d`；CYACC 0.25.5；#435／#475 已合併，部署流程不等於網站產品已重發 |
| CY Web deployed source／live | `b8bcc57524466c085f3642be67b816c7d0e9bd9c`；0.8.7 Build1；發布39058825249成功，active72／100%，見 §10.10 |
| CYACC 發布模式 PR | [#475](https://github.com/simonliu1118-byte/CYapps/pull/475)，已合併，merge `127acb7e2712e8fa3c196874f0037e1b7e1b506d`；PR validate／governance 已通過，PR deploy skipped；首次新入口發布待驗收 |
| Invoice 新基準 | #440 head `9c11db1cbf71c41d209b5cb0be1e1bc4c31ca4a2`、CY Web #151 head `a1f09d70239d2e23018ab5c6fec7eb27f916d586`，均 open draft；這些 SHA 只是查詢 checkpoint，不代表 D1 工作完成或已發布 |

### 9.2 目前 active 依賴與保護清冊

2026-10-18台灣02:40全量metadata重查為 **5 Workers、8 D1、2 R2**。第7個D1為既有CYACC隔離復原目標；本輪第8個DB08為Web受控部署準備的固定隔離還原目標，已被Web `RECOVERY_DB`引用且與主D1不同ID。兩者均PROTECT，並非一般Dev環境，不能當成重複資料庫清理。

| Alias | Active version number／產品宣告 | 儲存與私有服務依賴 | 處置 |
| --- | --- | --- | --- |
| CYWEB-EXISTING | 72／SOURCE_VERSION=0.8.7（Build1來源） | 原業務 D1；`IDENTITY` → CYID-P；已綁R2／GCS／獨立RECOVERY_DB，manual=true、schedule=false | PROTECT；來源版本已前進；最新網域／備份盤點見第 10 節，不回套舊版本 |
| CYACC-P | 240；main／既有部署證據產品為 0.25.5，未比對 bundle 逐位元指紋 | 原業務 D1、R2-01、隔離復原 D1；`IDENTITY` → CYID-P，`BACKUP_NOTIFICATIONS` → 同 Provider 的 `ConsumerNotifications` | PROTECT；不重做既有備份配置，不因有 binding 宣稱完整 DR 驗收 |
| CYID-P | 9；10/16 health 0.3.10 | 正式 Identity D1；APP_ENV=production | PROTECT；CY Web／CYACC 已共用此 authority；不重做 core portal 切換 |
| CYINVOICE-EXISTING | 95；CYID_ENABLED=true | 原業務 D1；`IDENTITY` 及 `IDENTITY_AUTHORITY` 都 → CYID-D，後者 `ConsumerAuthoritySync` | PROTECT；雙入口不是兩個 Worker，不先切換／停用 Provider |
| CYID-D | 122；10/16 health 0.3.7 | 舊 Identity D1；APP_ENV=development | PROTECT；Invoice 仍有依賴，不能先退場 |
| DB02 | 本次 5 個 active versions 未引用 | owner／外部與手動用途 UNKNOWN | PROTECT；未引用不是可刪證據 |
| DB08 | Web `RECOVERY_DB` active引用 | 獨立還原驗證副本；已從GCS還原97筆並核對reconciliation／Audit | PROTECT；已使用，不清空、不migration或自動換目標 |
| R2-02 | Web `BACKUP_R2` active引用；對照前批兩bucket及CYACC綁定確認Alias | Web專用；原manual97筆及pre_restore104筆已R2 readback verified；0物件屬先前歷史狀態 | PROTECT；正式備份owner與用途已核，保留既有provider與副本 |

兩套 CYID active metadata 均為 `EMAIL_PROVIDER=brevo`，並有 `BREVO_API_KEY` Secret binding；僅核對名稱／型別，不讀 key、不重設憑證、不發測試信。CY Web 與 CYACC 的首次登入 authority 阻點已解除，不代表所有真實登入／災復案例完成驗收。

CY Web 原發布分支含歷史 merge，因此 SHA 與 main 不同不等於 source 漂移。新 guard 要求事件 SHA=checkout SHA、當前 main 為 ancestor，且 tracked tree 完全相同；main dispatch 另要求 SHA 完全相同。CYACC 新發布分支則直接指向當前 main 完整 SHA，兩者均不靠 force-push 重寫來源。

### 9.3 當前發布入口與配置缺口

| 系統 | 目前 main／live 行為 | 本輪 source 整理／後續 Gate |
| --- | --- | --- |
| CYACC | #475 已合併；main 不自動部署，Environment 仍為 development | 使用 `deploy/cyaccountingweb-production` push 或 main dispatch；固定同目標 deploy 鎖、cancel=false、migration／資源準備前 exact main SHA guard；README 已有 Work 指令與成功判定；首次新模式發布仍待驗收 |
| CY Web | `deploy/cyweb-development` push 或 dispatch，另有唯讀 audit；Environment 仍為 development | [CY Web #178](https://github.com/simonliu1118-byte/chihyuan-web/pull/178) 已合併並於本輪首次 main dispatch 驗收：固定 deploy 鎖、cancel=false、dispatch main-only、current-main ancestry／tree guard；validate／audit 分鎖。原 Worker／D1／域名／Identity 與 health／schedule／Session／portal gates 均保留，見 §10.7 |
| CYID-P | 固定部署鎖與 cancel=false 已存在；成功部署證據已有 | 不重做部署鎖或假設舊 prerequisite 仍缺。正式 Worker 仍由 Dev 名稱推導，明確 Provider 輸入／Environment scope 另批處理 |
| CYInvoice | Actions 與 1 條 Workers Builds trigger 仍並存；其他 4 Worker 本輪均無 Builds trigger | 保留 enabled=true 的普通更新已修正，不重做此修正。等 D1 新基準後先驗收 Actions，再停用舊 Builds；目前不改 trigger 或舊分支 |

GitHub Environment 詳情可讀：已核對的 development scope `protection_rules=[]`、`deployment_branch_policy=null`。但 CYapps／CY Web repo 與 environment 的 Secret／Variable **名稱／scope 清單仍受 integration HTTP 403 限制**；10/12 UI 清單是歷史證據，不能當成目前名稱、值或 production scope 為空的證明。403 也不等於設定不存在，不要求重建同名 Variable／Secret。

後續 metadata 過渡順序：取得當前名稱／scope 與非敏目標映射 → 明確逐 App input owner → 新輸入仍指原 Worker／業務 D1／域名的 preflight → PR／CI → 已授權的同目標發布及 active read-back → 確認無引用／無回復需要後退出舊 metadata。Provider 繼續擁有 App／Workspace scope，不在 GitHub 複製第二份真值；無需為命名而重設 runtime Secret。Environment 搬移時須同時容許兩種核准入口，不增加阻擋 Work 的共通 Reviewer。

### 9.4 目前執行批次與完成判定

| 順位 | 狀態 | 工作／完成判定 |
| --- | --- | --- |
| A：主控對齊 | IN_PROGRESS | 更新既有 #73，標明已解決項目、當前來源與 API 界線；歷史 §6～8 不再作執行清單。文件 review／Governance Check 通過後待合併 |
| B：同目標發布 | IN_PROGRESS | CYACC #475 已合併，首次新模式發布仍待驗收；CY Web #178／#180 已合併，0.8.2 已完成 main dispatch／source guard／部署與線上驗收，見 §10.7。兩 App 的發布證據分開，不以 Web 成功替代 CYACC 驗收 |
| C：metadata／Environment | BLOCKED | 先補當前名稱／scope read-back；在原目標映射證明前不搬移設定。已準備 source PR 不因此停工 |
| D：CY Web 備份 | 手動驗收完成／排程HOLD | R2 owner與activebinding、Web新GCS key／required IAM四權限已核，manual已綁；同事件GCS retry雙端verified、GCS→固定隔離D1實際97筆reconciliation／Audit與104筆雙端安全備份均已通過。schedule依使用者決定維持false；已列Web TODO的正式上線前提醒，待其餘功能測試完成後再啟用／受控部署／cron回讀，詳§10.10 |
| E：Invoice 接續 | BLOCKED | owner 固定 D1 新 checkpoint：main／draft、active version、schema／profile、Device／Workspace／Audit、待發 migration、備份與相容回復；再做唯一入口、authority 接續與 #440／#151 聯測，不覆寫新工作 |
| F：舊設定／可選實體退場 | BLOCKED | 證明 owner、依賴為零及回復能力；設定退出與實體刪除分開。舊 CYID、DB02、R2-02 目前都不是可刪清單 |

上一批未全量重查流量、域名／routes、Cron 或備份內容；本批補上第 10 節的域名／routes、Cron 與 lifecycle，仍未讀備份內容，不把 10/12 聚合當成新證據。CYACC 既有本地／正式復原驗收應由 owner 最新交接引用，本輪未重做，不宣稱失效或已重驗。範圍足以準備本批 source PR，尚不足以宣稱 P1～P7 全部完成、正式權威整併或實體資源可退休。

### 9.5 發布護欄前批 source／文件驗證（歷史；最新見§10.10）

- CYACC #475：GitHub [validate](https://github.com/simonliu1118-byte/CYapps/actions/runs/38945121671) 與 [Governance](https://github.com/simonliu1118-byte/CYapps/actions/runs/38945121639) 成功；PR deploy skipped，網站未因此發布。
- CY Web #178：本機完整 Node suite 97／97；部署合約／canonical CYID 合成稽核、exact-sync／support window、rendered CI D1 migrations、Vite build、核對正確 CI bundle 後 dry-run、actionlint 及 Governance Check 通過。真實 Git history 測試涵蓋發布 merge、變更 tree 及過期 main 拒絕。遠端 CI／run 結果以 PR checks 為準，PR 不部署。
- 本檔與 CY Web source 已完成獨立唯讀工程審查；公開 Alias／資源識別字檢查與 diff whitespace 檢查通過。只更新既有 #73 主控文件，不變更 AITeam 產品、共通規則或版本。
- 已重查兩個 repo 的 development Environment：無 reviewer／branch 限制；repo 及 environment 的 Secret／Variable 名稱 API 仍 403。未搬移 metadata、重設 Secret、啟用備份、觸發部署／migration、切換 authority 或刪除資源。

## 10. 網域、排程與備份檢查（2026-10-17，Asia/Taipei）

### 10.1 範圍與最新狀態

§10.1～10.5 保留發布前盤點，最新部署與備份驗收結果見 §10.10。本次盤點只讀 Cloudflare 設定／metadata 與公開入口，不登入、不執行正式 SQL／restore、不讀備份內容或 object keys。使用官方 cf CLI；CLI 尚無對應命令的舊 Workers Domains／Schedules API，透過已安裝 cf 的 authenticated request adapter 僅發 GET，未讀取或輸出 credential storage。公開摘要維持 Alias；真實 domain、Worker／D1 名稱及 ID 的對照留在工作區。

CYACC #475 已於台灣時間 10/17 02:17 合併，main 自動部署已停用；這是已觀察到的主線更新，本批未執行合併或發布。CY Web 新主線／公開 health 為 **0.8.1**，active version **66**；source `72a832768243eacc1b190301abf75ff722149576` 與 main `e4d17640c9e1ba9682ccf72cad1e17242739ac3a` tree 相同且包含 main ancestry。五個 Worker 的 active deployment 均前後回讀穩定。

### 10.2 網域與資料責任

| 公開入口 Alias | Custom Domain／DNS | 實際處理與儲存 | 結論 |
| --- | --- | --- | --- |
| ACC-DOMAIN | enabled；proxied AAAA placeholder | CYACC-P → 原帳務 D1、獨立 RECOVERY_DB、R2-01；Identity／通知 → CYID-P | 無第二個 Web 服務或共用帳務 D1；隔離復原 D1 有既有用途 |
| WEB-DOMAIN | enabled；proxied AAAA placeholder | CYWEB-EXISTING → 原業務 D1；Identity → CYID-P | Worker 名含 dev 不表示另一個測試環境，公開 health 0.8.1／D1 ok |

兩個 Custom Domains 屬同一 Zone；該 Zone 本批查得 Workers route pattern **0**。Custom Domain 與其 proxied placeholder DNS 是正常的不同設定面，兩站 HTTPS 均可回應；不能把 placeholder DNS 或空 route 清單當成錯誤而刪除／另建 CNAME。

7 個 D1 中，**6 個實體 D1** 可對應到 5 Worker 的 active bindings，包含 CYACC 的 RECOVERY_DB；DB02 仍無本輪 active 引用，維持 UNKNOWN／PROTECT。Consumer 只透過私有 Service Binding 使用 CYID，不直接共用或讀取 Identity D1。Invoice 的雙 Identity 入口仍同指 CYID-D，留待其 owner 新 checkpoint。

### 10.3 HTTPS 觀察與下一個修正點

| 項目 | 實測／設定 | 解讀與下一步 |
| --- | --- | --- |
| ACC-DOMAIN HTTP `/login` | 308 → 同 host、同 path 的 HTTPS | 已觀察登入入口轉 HTTPS，保留現有流程 |
| ACC-DOMAIN HTTPS `/login` | 200；HSTS `max-age=31536000`；CSP `upgrade-insecure-requests; form-action https:` | 僅表示本輪已查的登入入口；未宣稱所有 HTTPS 回應均有 HSTS，health 回應未見同標頭 |
| WEB-DOMAIN HTTP `/` | **200，未轉 HTTPS** | 確認存在入口傳輸缺口；下一批優先準備 CY Web HTTPS 入口修正與回歸，不在本批直接改 Zone 或產品 |
| WEB-DOMAIN HTTPS `/` | 200，未見 HSTS | 與 HTTP 入口一起處理；本輪未提交登入資訊或建立 Session |
| 共用 Zone 設定 | always_use_https=off、min_tls_version=1.0、tls_1_3=on、ssl=full | 這是設定值，未測 TLS 1.0／1.1 實際協商；Full 不構成 Workers 有不安全外部 origin 的證據。全 Zone 強制或提高最低 TLS 前，先盤點其他 host／相容需求，避免影響未核對服務 |

下一批 HTTPS 驗收至少涵蓋：HTTP 頁面在載入登入程式前導向相同核准 host 的 HTTPS；HTTPS 登入／主頁的 HSTS；原 Session／Email 驗證／recovery 與 static assets 不回歸；Local Wrangler 的 loopback 開發仍可用。Source PR 與正式發布／Zone 設定修改分開，不先假定現有 assets routing 一定經過 Worker。

### 10.4 Cron、R2 lifecycle 與副本聚合

| Alias | 實際 Cron／active 配置 | 本輪 metadata | 結論 |
| --- | --- | --- | --- |
| CYWEB-EXISTING | `0 19 * * *`＝台灣每日 03:00；沒有 active BACKUP_R2、BACKUP_ENABLED、BACKUP_SCHEDULE_ENABLED、GCS bucket／credential bindings | 這是程式清單更新 Cron，並非備份；候選 R2-02 0 物件 | **應用層 R2＋GCS 備份未啟用**，不等於所有平台回復機制均不存在；不從有 Cron 推定備份可用 |
| CYACC-P | `30 19 * * *`＝台灣每日 03:30；topology=parallel_dual_provider，R2 與 GCS Secret binding 名稱／型別存在 | R2-01 **48 物件／11,474,602 bytes**；最新 upload 為台灣 **10/16 03:30:06**，與本輪查詢前最近一次已到期的排程時段一致 | 只證明 metadata 與時序相符，不證明本輪備份內容、GCS delivery 或自動健康通知已驗收；不以物件數推算 logical backup sets |
| CYID-P／CYID-D／Invoice | schedules 為空 | 本批只核對排程設定，不觸發 handler | 不因此判定資料無用途或服務可刪 |

兩個 R2 bucket 均為：enabled 的全 bucket **45 天 object deletion**，以及獨立的 **7 天未完成 multipart upload 中止**；後者不表示完整備份 7 天刪除。45 天長於現行 App 的 30 天 R2 application retention，符合第二層 safety expiry 方向。兩 bucket 的 r2.dev 公開入口均 disabled，自訂 bucket domains 均為空；不把 Worker 的 private R2 binding 誤認成公開檔案網址。

R2-02 雖名稱／歷史規劃提示 CY Web 候選，但 active 無 binding、目前為空，仍不足以證明 owner／用途及可直接綁定。GCS 端實際 bucket policy、IAM、物件及 credential 可用性本批未查；CYACC 既有 10/15 真實 GCS→隔離 D1 演練保留為 owner 歷史證據，沒有重做，也未將其抹成未曾驗收。

### 10.5 Source／本機隔離驗證與可執行下一批

CY Web 0.8.1 main 的既有備份程式只有全部必要 inputs 就緒才建立服務，排程還須另行 opt-in；R2 30 天、GCS 182 天，GCS 排程日為台灣週三／週日，手動及 pre-restore 同時要求兩個 provider。內部 isolatedRecovery service／本機驗收要求 SUPER_ADMIN、雙確認、相同 Workspace／schema，以及隔離空 D1。目前公開備份 API 僅接 list／create／retry，尚無 production 隔離還原操作入口；這些內部限制不等於正式操作入口已完成，不能直接用來宣稱可執行正式 DR。

- 本機在上述 main SHA 執行 **7／7** backup provider／schedule tests 通過，GCS 為合成 transport，未使用正式 credential。
- 原有 Wrangler isolated Worker＋D1 runner 套用 **13 個 migrations**，**17 個 acceptance checks 全通過**，包含 `portableBackupRecovery`、`tieredBackup`、`isolatedRecovery`。只在臨時本機 D1 寫入合成資料，不是正式備份內容驗證或 production restore。
- 下一批先處理 CY Web HTTPS 入口的 source／設定方案；其後確認 R2-02 owner、核准 GCS 目標／最小權限及原 D1 的備份範圍，以原程式完成一次受控備份與隔離還原，再開例行備份排程。
- GitHub 設定權限缺口仍獨立存在：新增只讀網路 Secret 已保存並出現在 runtime，但同一路徑仍回 integration 403，未能確認新 token 真正生效。不要求重建或擴權，也不把此阻點擴大成 Cloudflare metadata 不能查。
- 本批回寫仍只有本份主控文件；不修改 DNS、TLS、Worker、D1、R2 policy、GitHub Settings 或產品 source，不部署、寄信、SQL 查正式資料或刪除任何資源。下一批部署與備份啟用仍依各自的目標核對與當次指示執行。

### 10.6 HTTPS 修正與備份配置準備（同日續作）

使用者已指示開始修正。本批產品 source 沿用各 Repo 的 branch／PR；主控仍只更新本檔，不另建協調真值。

- **HTTPS source：** [CY Web #180](https://github.com/simonliu1118-byte/chihyuan-web/pull/180)，head `a5d44af6c428cce7fc1d94caf31ad8ddf9657ef4`，來源版本 **0.8.2／Build 0**。所有請求先經同一 Worker transport 邊界，公開 HTTP 在讀取 body／binding 前 308 至同 host／path／query 的 HTTPS；公開 HTTPS 加一年、僅限目前 host 的 HSTS。API 保留原路由與 JSON fallback，非 API 才交 ASSETS 提供原 SPA／靜態檔；loopback 開發保留 HTTP。
- **發布驗收：** workflow 新增正式網域與 workers.dev 的 HTTP→HTTPS、HSTS、首頁 HTML 與 built JS 檔案類型檢查，避免 SPA fallback 的 HTML 200 被誤認為 JS 成功；保留原 source／consumer／D1／Session／schedule gates。與發布流程 [#178](https://github.com/simonliu1118-byte/chihyuan-web/pull/178) 分開，#180 不代替固定鎖／main ancestry／tree guard 的審查。
- **取捨：** 首頁及靜態檔必須先 invoke Worker，會增加相應 Worker request 用量；ASSETS 保留原回應及 cache headers。不修改共用 Zone、DNS 或其他 host。
- **本機證據：** 最終 head 完整 Node **106／106**，含新增 **10** 項 transport／deployed-smoke 檢查及實際 Miniflare asset router。Browser／Worker TypeScript、source contracts、13 migrations／17 isolated Worker＋D1 acceptance checks、Vite build、rendered CI bundle dry-run、actionlint 及本機 Governance Check 通過；localhost 實際首頁／API／模組均 200，沒有 HSTS 或轉址。MIME 審查修正後重跑完整 Node suite；未重新執行正式登入或寄信。
- **備份準備：** CY Web 部署文件已列出受保護的 R2／GCS inputs、bucket 級最小權限與先手動雙副本／隔離還原再開排程的步驟。R2-02 owner、核准 GCS bucket／credential 位置仍待確認；不讀取或沿用其他 App 的 Secret。另須完成 production 隔離還原操作入口，並以獨立空 D1 驗收，不能部署 acceptance Worker 代替。備份啟用與例行排程仍未完成。
- **遠端 CI：** #180 head `a5d44af6c428cce7fc1d94caf31ad8ddf9657ef4` 的 [Runtime Check 38955619793](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/38955619793)、[部署合約 38955619833](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/38955619833)、[Governance 38955620189](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/38955620189) 均 success。PR 的 deploy／audit jobs 按設計 skipped，並未發布。已完成獨立 source／交接文件審查。
- **發布前紀錄：** 本小節形成時 #180 為 draft，尚未合併、發布或更動 runtime bindings；後續授權部署結果見 §10.7。正式環境仍以 §10.1 最近回讀的 0.8.1／active 66 為基準，不能把 source 0.8.2 當成已上線。下一步完成 source CI／審查，再按核准的同目標發布入口驗收；備份另待上述配置與操作 Gate。

### 10.7 CY Web 0.8.2 已部署／首次新模式驗收（2026-10-17 13:59，Asia/Taipei）

使用者明確指示「可以部署，你確認完沒問題就可以部署下去」。先合併 #180 至 `26f8de1a2ce073d2eaa55609146398645dee5731`，再同步 #178，解決測試 path 清單與 TODO 衝突，保留兩邊內容；沒有新增產品／schema／備份啟用變更。同步 head `eda094e194f083b0a89adb5ab2f374a6211eab7c` 的本機 Node **110／110**、部署 config contract、actionlint、提交後 Governance 與獨立審查均通過；遠端 [Runtime 39008108810](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39008108810)、[部署合約 39008108836](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39008108836)、[Governance 39008108796](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39008108796) 均 success，才合併 #178。

- **正式部署來源：** 當次已驗證的 main `f70cc561d1f373a93381eb93a0ab59103371af9b`，產品 **0.8.2／Build 0**，consumer **1.0.5**。使用既有 `development-deploy.yml`、main dispatch、`deploy-development` operation；[正式部署 39008172352](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39008172352) 於 **13:59:43 Asia/Taipei** 完成且 success。validate 與 deploy job 都成功；獨立 audit job 依 operation skipped，deploy job 內的 canonical production portal strict gate 已成功，不把 skipped 誤報為執行。
- **護欄實際執行：** 來源 guard 在遠端 migration 前通過，固定同目標鎖且 cancel=false；canonical production resolver 保持原業務 D1／Workspace／core App。每日程式清單 Cron、雙入口 exact source／consumer／D1 health、HTTP→HTTPS／HTML／built JS MIME／HSTS、兩入口無效 Session 401 與清 cookie、正式 Identity strict gate 均成功；未新增 migration。
- **Cloudflare 回讀：** 原 Worker active version **67**、單一版本 **100%** 流量。相對發布前 version 66，原業務 D1、Identity／其他 service、Secret binding 名稱及備份 opt-in contract 相同；備份仍未啟用。不是建立第二套 Worker／D1，亦未修改共用 Zone、DNS、R2 policy 或 Brevo。
- **獨立線上回讀：** 正式網域與 workers.dev 的 health 均為 **0.8.2／consumer1.0.5／f70cc56…／D1 ok**；HTTPS 首頁／登入頁／實際 JS 與 API 均有一年 HSTS，未知 API 保持 JSON404；隨機無效 Session 仍401並清 cookie。此雲端代理對 plain HTTP 關閉 socket，因此本機包含 HTTP 的額外 smoke 未通過；HTTP exact redirect 的證據使用本次 GitHub runner 已成功的正式部署 step，HTTPS 的獨立檢查則全通過，未略過或修改 repository 驗證。
- **後續：** CY Web HTTPS 與首次新模式發布已完成。備份的 R2 owner／核准 GCS 目標及 credential 位置、正式隔離還原操作入口與實際雙 provider 演練仍待完成；本輪不代表備份／災復已就緒。沒有建立 tag／Release、執行正式 restore、寄信、切換 Invoice authority 或刪除資源；亦未部署 CYACC，該 App 首次新模式發布仍待其自身驗收。

### 10.8 CYACC Phase D 參考與 CY Web 啟用前修正（2026-10-17）

使用者確認 CYACC Phase D 已運行，要求 CY Web 在檢查通過後沿用同模式，並提供專用 Web GCS bucket 非公開／目前無可見物件的畫面。實際資源真名僅保留於受保護工作區，不重建 bucket，也不沿用另一 App 的廣權限 key。

- **參考已成立：** CYACC active version 240，本輪重查具有 R2／GCS／RECOVERY_DB／通知 bindings。其 Phase D 由 append-only policy event 啟用：台灣每日03:30，R2每日30天、GCS週三／週日182天，manual／pre-restore雙副本；14次Phase C、真實雙provider隔離驗證與GCS→獨立D1還原證據沿用原 owner `BACKUP_ARCHITECTURE_HANDOFF.md`／`WORK_HANDOFF.md`，不重跑已完成演練或改動 CYACC。新政策 GCS 的實際週期排程觀察仍依該 App 自己的最新證據，不由本輪 binding metadata 代替。
- **Web 的實際差異：** 原排程與 retention 已相同，但 R2 寫入失敗便 return，會抑制當次應執行的 GCS。已建立 [CY Web #182](https://github.com/simonliu1118-byte/chihyuan-web/pull/182)，head `ab58fb5f5b15429310b653c5744c4dee07cbe722`，source **0.8.3／Build 0**，讓同一份單次 export 的兩 provider 各自嘗試、驗證、稽核，UI 正確提示 GCS-only 成功。普通 R2-only 日不多建 GCS；retention 仍要求同provider的新副本 verified，pre-restore 仍要求安全副本雙端 verified。
- **驗證：** 新增 actual Worker＋D1 故障回歸先在舊 source 重現 `ACCEPT_BACKUP_R2_OUTAGE_BLOCKED_GCS`；修後13 migrations／17 acceptance checks通過，涵蓋manual／pre_restore／週三R2故障、未排定GCS日、雙失敗及GCS-only安全副本不得寫入隔離目標。Node110／110、Browser／Worker typecheck、source contracts、Vite build、提交後Governance及獨立工程審查通過；head `ab58fb5` 的遠端 [Runtime 39008922077](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39008922077)、[部署合約 39008922115](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39008922115)、[Governance 39008922088](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39008922088) 均 success；PR deploy skipped。
- **配置界線：** Web GCS bucket 由使用者畫面確認；後續 GitHub `development` Environment 畫面也確認該 scope 沒有 `GCS_SERVICE_ACCOUNT_JSON` 或備份 variables，需透過 GitHub 受保護設定補齊；IAM／lifecycle仍待確認。這不等於其他scope均不存在。13:59那一次Web deployment runner 的 `GCS_SERVICE_ACCOUNT_JSON` 注入值為空，只核對存在狀態、不讀值；這不證明現在所有scope都沒有Secret。GitHub名稱API仍403，不要求重建token或讀另一App的key。
- **容量與啟用：** 對原業務D1僅做aggregate SELECT，確認目前未超過exporter的500總筆數，0列寫入、不讀取業務內容；5MiB限制仍由實際export驗證。paired copy、正式隔離還原操作入口及實際驗收就緒後才開排程。Web正式環境仍為§10.7的**0.8.2／active67**；#182尚未合併／部署，備份與新增排程未啟用，03:00仍只是catalog。不能以CYACC既有成功替代Web自己的provider／restore驗收。

### 10.9 新 GCS 設定驗證與 Claude 主線接續（2026-10-18，Asia/Taipei）

使用者回報已新增 Web GCS JSON Secret 與 bucket variable，要求試驗；期間告知 Claude 正在做0.8.4，因此先暫停push／merge／deploy，直到使用者確認已部署才恢復。沒有覆蓋或重部署該介面工作。

- **本階段首次回讀（後被下列0.8.6取代）：** public health 回讀 **0.8.4／consumer1.0.5／dbdf111b30face7fb99b68069b305665da97be05／D1 ok**，Cloudflare active **68／100%**。原業務D1／正式Identity仍保留，無active備份bindings；這是本輪讀回，不以 source VERSION 代替 live。
- **已驗 source：** 同步最新供應商／進貨與主題主線後，本輪採下一版 **0.8.5 Build0**。[#182](https://github.com/simonliu1118-byte/chihyuan-web/pull/182) head `f6963de65b8f00524b2ddacd9f2a07340ca72ca2` 通過Node117／117、14 migrations／17 actual Worker＋D1 checks、typecheck／source／build／actionlint／Governance及獨立review；[Runtime39056401970](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39056401970)、[部署合約39056402000](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39056402000)、[Governance39056401956](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39056401956) 均success後合併至 `93bc3ca11dc3d596f88d4ea20fd51c824a31b1a6`；當時尚未部署，後由下列0.8.6發布一併帶入。
- **只讀驗證：** main dispatch `audit-backup` 與部署job隔離，只用Web自己的GCS JSON做結構／RSA檢查及固定JWT流程，接GCS bucket `iam/testPermissions` 驗證object create/get/list/delete。沒有object讀寫、SQL、CF管理token、Worker設定／排程變更；bucket經workflow_call secrets邊界遮罩，flag走非機密inputs，僅輸出boolean與stablecode，private temporary0600檔finally清理。
- **首次結果與修補：** [audit39056485585](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39056485585) bucket存在、JSON Secret注入為空，code=`BACKUP_GCS_INPUTS_MISSING`；尚未測到真實key／IAM，不能據此斷言key失效或使用者未設定。[#185](https://github.com/simonliu1118-byte/chihyuan-web/pull/185) 補顯式Repository Secret轉送，保留callee Environment Secret覆蓋；producer僅輸出Secret可見／誤放Variable的兩個存在boolean。actionlint／Governance／獨立審查及遠端部署合約／Governance均通過，合併 `5ae3a2a84214d4506fe440bb9bc43b1013aec875` 後重跑audit，無產品runtime／版號更動。

- **GCS新key實測成功：** 顯式轉送後 [audit39056878172](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39056878172) success：JSON與bucket存在、真實JWT／bucket四項object權限通過，`gcsAccessVerified=true`；當時三個R2／opt-in variables仍未設定，因此 `manualConfigurationReady=false`，沒有誤報為備份成功。原始log僅核對allowlist boolean/code，無bucket真名或private key。GitHub變數寫入API仍403且未變更，使用者隨後由GitHub development UI補R2 bucket、backup=true、schedule=false。
- **最新手動模式已部署：** 來源 `5ae3a2a…` 的 [39057334816](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39057334816) 因Claude新0.8.6已合入main，被 current-main guard 在remote changes前攔下。改採最新 `b474f046073461a189aecc6fd8f150bb62a29875` 的 [39057509990](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39057509990) success；Cloudflare回讀 **0.8.6／consumer1.0.5／active69／100%**，原業務D1與正式Identity相同，R2、GCS Secret已綁定，backup=true、schedule=false。部署內source／D1health／HTTPS／invalid Session／catalog cron／production portal全通過，保留Claude的compact UI與remember-me修正。
- **真實副本未全通過：** 使用者以正常SUPER_ADMIN介面於台灣02:23:27建立manual事件；97筆／33,064 bytes，R2 verified、GCS failed。只讀aggregate catalog回讀一致，既有last_error只給泛化碼，尚不能判定是key、寫入、讀回或服務問題。保留事件與已驗R2，以固定安全診斷及同事件GCS retry定位；未建立新事件掩蓋失敗，也未啟用排程。
- **隔離還原準備：** 以最新0.8.6為基準另做0.8.7 source，沿用既有BackupService／portable restore／Audit，提供真實SUPER_ADMIN同源雙確認與明確GCS讀取的固定RECOVERY_DB入口。部署先核來源，再準備不同ID之固定空D1；既有目標需此Worker的binding證明owner，名稱相同不接管、非空不migration、不reset或自動換目標。source／review／CI與真實GCS retry、paired backup及隔離reconciliation仍在進行；不得把本機成功代替production驗收。

### 10.10 CY Web 受控隔離還原入口與 GCS Worker 相容性返修（2026-10-18）

- **入口source已發布：** [#187](https://github.com/simonliu1118-byte/chihyuan-web/pull/187) head `8837348025e80d26ec71dc19cea04c2b673de7e6` 通過128 Node、14 migrations／17 actual Worker+D1 checks、Chromium1280／390雙確認／busy關閉保護、typecheck/source/build/config/actionlint/Governance及獨立review。遠端 [Runtime39058283550](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39058283550)、[部署合約39058283623](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39058283623)、[Governance39058283541](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39058283541) 全success後合併 `8cca0379a13b7392a1af6bcd0e3b91c3c4ab442a`。授權 [deploy39058390526](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39058390526) success，回讀0.8.7／active70／100%，已準備固定獨立RECOVERY_DB（不同主D1），原主D1／Identity一致，backup=true、schedule=false。這僅代表入口已接線，不是還原已成功。
- **GCS真正根因已重現：** actual workerd執行原GCSProvider的預設sender時，bare fetch儲存於this.send再呼叫，native fetch收到錯誤this，Illegal invocation發生於送Google請求之前；修receiver後又重現workerd只支援manual/follow，不支援redirect=error。先前Node實際key/IAM檢查能通過，因Node沒有這兩項執行環境限制；合成provider mock亦不能代表native Worker fetch。
- **同項目Build1：** [#189](https://github.com/simonliu1118-byte/chihyuan-web/pull/189) head `101a706386f6de4a1049396715c1dd4ab550b09d` 基於Claude最新置中登入／remember-me修正 `f5531f5…`，保留其變更，source **0.8.7 Build1**。改global receiver，manual模式明確拒絕全部3xx；JWT/Bearer不跟址，固定端點、timeout/bytes/integrity保留。actual workerd default nativefetch+WebCrypto驗JWT/IAM/upload/exact readback及OAuth302單次拒絕；合成key僅記憶體。131 Node、typecheck/build/actionlint/Governance／獨立review及遠端 [Runtime39058741101](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39058741101)、[部署合約39058741124](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39058741124)、[Governance39058741118](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39058741118) 全success，合併 `b8bcc57524466c085f3642be67b816c7d0e9bd9c`。授權Build1部署 [39058825249](https://github.com/simonliu1118-byte/chihyuan-web/actions/runs/39058825249) **success**，Cloudflare active **72／100%**、exact source／0.8.7／consumer1.0.5，原主D1／Identity／隔離目標binding保持一致，backup=true、schedule=false。雙HTTPS入口／實際JS資產MIME／HSTS／health／未知API JSON／無效Session清cookie另行回讀全部通過；HTTP redirect與canonical production portal採同run成功gate證據。
- **驗收與保護：** 保留02:23:27的原manual事件與R2副本；Build1上線後已由正常SUPER_ADMIN完成同事件GCS retry、雙端verified及GCS→固定獨立空目標還原，證據如下。沒有模擬正式principal、取得raw Session、使用其他App key、切主D1、重建bucket或清空目標。排程保持false，真實provider／restore通過後才另行啟用；不能以本機或CYACC成功代替。

- **真實GCS與隔離還原已通過：** 使用者正常SUPER_ADMIN於台灣02:38:23對原02:23:27事件執行GCS retry；aggregate catalog回讀同一事件仍97筆／33,064 bytes，R2及GCS皆verified、errorCode=null，未重匯出。台灣02:39:55明確選GCS作隔離還原，建立pre_restore安全備份104筆／34,359 bytes，雙端verified後才還原。正式Audit有authenticated actor的`backup.rehearsal.started`與`backup.rehearsal.verified`，provider=gcs、target=isolated_empty_database，記錄安全事件關聯；固定獨立目標aggregate durable count=97，與來源事件一致。只讀metadata/count核對，不把正式資料／principal／payload帶入Public source。目標使用後保留。
- **清冊與排程界線：** 02:40重查5Workers／8D1／2R2，Web recovery目標已列冊；目前Web bucket與原兩bucket清冊／CYACC binding對照確認為R2-02，已不再是未引用／0物件。cron回讀仍僅`0 19 * * *`，沒有backup cron。GitHub變數寫入API403，曾請使用者改設定；使用者明確選擇先維持false，因尚有其他功能測試待完成，要求寫TODO於正式上線前提醒。已在 [Web TODO](https://github.com/simonliu1118-byte/chihyuan-web/blob/main/TODO.md) 記錄上線前提醒與改true→受控部署→cron回讀→首個真實scheduled事件觀察。當前manual正常、schedule=false；不得把備份／還原成功當成排程已開或首個scheduled事件已發生。
