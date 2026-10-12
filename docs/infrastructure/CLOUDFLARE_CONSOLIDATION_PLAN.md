# CY 系列跨 Repository Cloudflare／GitHub 整併主控計畫

> 建立：2026-10-12（Asia/Taipei）。**AITeam 是本次跨 Repo 工作的唯一主控／進度來源；本檔是一次性工作計畫，不是第四層永久規則，不取代任何目標 Repo 的 REPOSITORY_RULES.md／REPO_POLICY.md／PROJECT_RULES.md。**
>
> 管理範圍：simonliu1118-byte/CYapps（Public）、simonliu1118-byte/CYapps_PVT（Private）、simonliu1118-byte/chihyuan-web（Public）。AITeam 本身是協調文件的儲存庫，不算第四個待整併的產品部署。
>
> 目標：採**一套正式 Cloudflare 運行環境**＋Local Wrangler/D1 與 GitHub CI 作為主要開發測試方式，避免為每個小程式建立 Dev／Production 成對 Worker/D1。Existing Dev 有實際 Employee／Workspace／Device／業務資料者，在真正完成遷移及驗收前必須保留，不依名稱推斷可刪除。

## 0. 使用者已定案的限制與目的

1. **CYACCweb main 自動部署必須保留。** 這是明確保留的便利性：現階段頻繁修改時，不改為手動批准，也不搬入需要 Required Reviewers 才能部署的 GitHub Environment。等使用者日後確認開發頻率降低，再重新評估。必須保留自動驗證／正式 login 與前端資產 smoke。
2. **不再建立、上傳、刪除重試遠端測試 Worker／probe Version／清理 handler。** CYInvoice 過去遇到 GET 仍可讀取、DELETE 404／10339，無法安全確認已清理。保留歷史證據、停止遠端測試程式部署、停止額外權限擴張；Local/CI synthetic tests 正常保留。
3. 目標「單一正式雲端」是**邏輯環境收斂**，不是把所有 App 及 D1 合成一個。CYID 的 Identity D1 與各 App 業務 D1 仍依責任分離；小 App 無雲端資料需求不需 D1。
4. CYInvoice 的 IDENTITY（Fetcher／Login、Resolve、Logout）和 IDENTITY_AUTHORITY（同 CYID Worker 的 ConsumerAuthoritySync Named RPC，用於離線 Employee 快取失效比對）不是兩個 Worker 或 D1；目前維持兩個入口。
5. CYInvoice 原 Device、Device Token、Workspace、發票業務、歷史與原 Cloud D1 必須保留；CYID Development／Production 的 Employee、Credential、Role、App Access、Session 也不可直接覆蓋合併。
6. 文件先進 AITeam PR。各目標 Repo 執行時仍走各自 branch／PR／CI。**本計畫不等於已批准直接做正式身分切換、D1 資料遷移、Worker／D1 刪除、Secret 重設或 Release。** 進到此類操作前必須完成該階段 Gate 並核對可回復證據。

## 1. 唯讀來源盤點（不是 Cloudflare Dashboard 的即時清冊）

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
- 不取消 CYACCweb 自動發布；不把 GitHub Environment Reviewer 強迫套用到 CYACC。
- 不重啟過去刪不掉的 Worker/Version 清理專案，也不因本輪打開高權限 Cloudflare Token。
- 不在 Public AITeam Git 中保存真實 Worker Name、D1 UUID、Workspace/Employee ID、Customer/Invoice data 或 Private Repo Key。

## 3. GitHub Environment／Variables／Secrets 整理方案

**注意：此表只列 GitHub workflow 在原始碼中的引用，尚未直接讀取 GitHub Settings 的實際 Variable/Secret 名稱或 scope，不能誤稱已盤點完成。**

| Repo／用例 | 原始碼可確認的引用 | 建議 |
| --- | --- | --- |
| CYapps／CYID development | secrets.CLOUDFLARE_API_TOKEN、secrets.CLOUDFLARE_ACCOUNT_ID、secrets.CYID_WORKER_NAME、vars.CYID_D1_DATABASE_ID | 此舊來源先保留，直到跨環境權威差異與安全切換完成；Provider 原正式/開發 Binding 必須讀回核對。 |
| CYapps／CYID Production | 仍使用 environment: development；由既有 CYID_WORKER_NAME 推導 Production，另有 vars.CYID_PRODUCTION_CORE_CONSUMER_WORKER_NAME | 將正式目標改為**明確的來源**，不靠 Dev 字尾推導；以原 Cloudflare 配置 read-back 做嚴格比對，確定無誤才遷移 GitHub Environment。 |
| CYapps／CYACCweb | vars.CF_WORKER_NAME、CF_D1_DATABASE_NAME/ID、CF_R2_BACKUP_BUCKET、CF_BACKUP_TOPOLOGY、CF_CYACCOUNTINGWEB_CUSTOM_DOMAIN；secrets.CYID_WORKER_NAME 與 Cloudflare credentials | 保留 main 自動部署。搬動 Environment 時新設定不能加 Required Reviewers；先 read-only 綁定核對，確定不存在名稱 collision 才考慮重新命名。 |
| CYapps／CYInvoice | secrets.CYID_WORKER_NAME、Cloudflare credentials；Workspace mapping 與 scope 從 Provider／original business D1 read-back | 不把真實 Employee/Workspace/Device secrets 作第二份 GitHub 真值；保留 CYID_ENABLED 與原有 D1 Device continuity。 |
| chihyuan-web／CY Web | vars.CF_WORKER_NAME、CF_D1_DATABASE_NAME/ID、CF_IDENTITY_SERVICE、CF_IDENTITY_APPLICATION_ID、CF_IDENTITY_WORKSPACE_ID、CF_BACKUP_*、GCS_BUCKET；相應 Cloudflare/Backup Secrets | 本 Repo GitHub Environment 與 CYapps 獨立；選定最終 CYID 目標後再收斂自身 Deployment Metadata。 |
| CYapps_PVT | main 無 Cloudflare deploy workflow | 不先建立空的 Cloudflare Variable/Environment，只檢查是否存在實際手動／外部部署。 |

**已完成的歷史工作不是新的 TODO：** 2026-10-12 既有文件記載使用者已刪除 development Environment 與 Secret 同名的 CYID_WORKER_NAME Variable，保留 Secret，並在 CYInvoice read-only preflight 通過。不得又要求新建同名 Variable 或推定它仍待清理。

### Production Environment 與 CYACC 自動發布

- 最終建議按 Repo 建立清楚的正式部署環境邊界，但不強迫每 App 有成對的長期 Dev+Prod。
- **若同一 GitHub Environment 設置 Required Reviewers，CYACCweb 自動部署也會被擋住。** 現階段可採「production Environment 無共通 Reviewer；CYACC main 自動部署；高風險 CYID/CYInvoice Workflow 自己用 workflow_dispatch + 當前 runtime read-back gate」。
- 之後若特別要為 CYID 設 GitHub Environment 內建人工 Reviewer，再拆出**少量必要**的 CYID 專用 Protected Environment；CYACC 不搬入需要人工核准的環境。
- CYapps 多 App 共用 Environment 時，CF_WORKER_NAME／CF_D1_DATABASE_ID 等**泛名可能互相覆蓋**。若統一 Environment，需使用 per-App 前綴變數或確定版本的 Runtime Read-back；不應一邊改名字一邊讓 CYACC 自動部署到不明目標。
- Cloudflare Runtime Secrets 以保留原值、檢查存在與類型為主。GitHub 的 Secret 只有部署必要時才存，不在 Public Repo 或 log 直接回顯；權限縮減／金鑰輪替是後續另外評估項。

## 4. 階段、Gate 與可回復條件

狀態只用 DONE / IN_PROGRESS / READY / BLOCKED / PENDING。任何執行者先讀本文件**最新 main**、再按目标 Repo 規則與現況核對，不能用對話記憶替代。每個階段結束要更新本文件的證據與下一步。

| ID | 狀態 | 工作 | 驗證 Gate | 安全限制／回復 |
| --- | --- | --- | --- | --- |
| P0 | IN_PROGRESS | 在 AITeam 建立這份跨 Repo 主控文件及追蹤入口 | PR 只有 docs 修改，沒有更新產品版本／規則／Workflow／Secrets | 可撤回 PR，不影響現行部署 |
| P1 | PENDING | Cloudflare **唯讀**取得 Worker/Active Version、Custom Domain、D1 Binding、R2、Cron、Service Binding、Requests；GitHub 三 Repo Environment 和 Variable/Secret **名稱**、scope、branch／approval rules | 每個活躍資源有 owner、引用、狀態與回復風險；查不到的標 UNKNOWN | 不顯示 Secret 值；不建立/刪除任何資源 |
| P2 | BLOCKED on P1 | 對照當前實際 deployed version、三 Repo main workflow、全部 Consumer Binding；修正清冊來源證據 | 清楚哪些是真 Dev、哪些名稱 Dev 但承擔真實資料／用戶 | CYACC main 自動 Deploy 不變 |
| P3 | BLOCKED on P1/P2 | CYID Dev/Prod 資料與權威差異：Employee、Workspace、Credential 啟用狀態、Application Registry、Role/App Access、Consumer Mapping（只取非敏聚合與可核對 fingerprint） | CYACC/CY Web/CYInvoice 都有正式切換方案及否決/回復條件 | 不直接複製 Dev D1 覆蓋 Production，也不移出密碼 verifier/Session token |
| P4 | BLOCKED on P1/P2 | 逐 Repo PR 收斂 GitHub Deploy Inputs；正式 Worker 名稱明確化、避免泛名 collision、設定來源唯一化 | PR CI、Config preflight、read-back 與既有 target 100% 一致；CYACC auto push/deploy 保留 | 不先變更 Cloudflare Binding、不添加強制 Reviewer 或遠端測試 Worker |
| P5 | BLOCKED on P3/P4 | 分 App 做 CYID 權威／App Access／Workspace 安全接續；特別保護 CYInvoice Device/Token/Offline/業務歷史 | 真實登入/Session/Role/Access/revoke、Device／Offline/reconnect 和資料連續性驗收；資料備份及回復證據 | 每次只切一個 Consumer，遇歧義停下，不自動重建 Workspace／Employee |
| P6 | BLOCKED on P5 | 回收不再使用的 Dev **部署入口**及多餘 Variables/Secrets／branch triggers | 連續期間無活躍依賴、來源引用歸零、回復方案仍可執行 | 不在此階段刪實際 Worker/D1，也不重試 10339 |
| P7 | BLOCKED（另需明確資源清單與核准） | 如確有價值，評估歷史實體 Worker/D1 的個別退場 | 有 owner、可恢復快照、影響/成本證據及實際目標逐一核准 | 未達 Gate 永久保留，不盲刪 |

### 公開文件中的匿名資源表（只用 Alias，不放真實資源 ID）

| Alias | Owner | 已知保護原因 | 真實 read-back | 處置 |
| --- | --- | --- | --- | --- |
| CYID-P | CYapps | CYACC 正式 Identity authority | 未完成 | PROTECT |
| CYID-D | CYapps | CY Web／CYInvoice 開發線 Identity 依賴 | 未完成 | PROTECT UNTIL MIGRATION |
| CYACC-P | CYapps | 正式帳務 Worker/D1/Backup 與 auto deploy | 未完成 | PROTECT |
| CYWEB-EXISTING | chihyuan-web | 現存 Web/業務 D1/入口 | 未完成 | PROTECT |
| CYINVOICE-EXISTING | CYapps | 原 Workspace／Device／Token Hash／Invoice History | 未完成 | PROTECT |
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
CYACCweb main 自動部署是否仍正常：
是否建立遠端測試 Worker／Version（預期：否）：
CYID Authority／CYInvoice Workspace/Device/Data Continuity：
阻擋原因／未驗證事項：
Recovery 前置條件或參考（不含 Secret／ID）：
下一個 Gate／工作：
~~~

### 2026-10-12 15:36 (Asia/Taipei) — P1 唯讀連線再次受阻

- 使用者已明確授權在需要時透過安全的瀏覽器互動自行登入 Cloudflare 與 GitHub；但**本次沒有進入登入階段**。Browser Use 回報 `insufficient_credits`，要求補充 Browser Use 專案額度，重試無效。**這不是驗證失敗、也不是缺少使用者登入授權。**
- 上一輪無登入工作階段且瀏覽器導覽回報 `ERR_TUNNEL_CONNECTION_FAILED`。兩輪都**沒有讀到** Cloudflare Dashboard 的 Worker／D1 清單、Service Bindings／Metrics，也沒有讀到三個 Repo GitHub Settings 的 Environment、Secret/Variable 名稱或保護設定。
- Source-only 的 GitHub workflow/設定引用仍有效，但不能當作目前雲端實體資源數量或 Settings 實際設定的證據。**P1 繼續 PENDING／BLOCKED ON LIVE READ ACCESS；P2～P7 不因此視為通過。**
- 可接續路徑：獲得可用的唯讀瀏覽器工作階段、專案瀏覽器額度，或其他**受核准且不輸出 Secret**的唯讀管理 API／清單後，再補實際資源的匿名摘要。不得為查詢方便新建遠端測試 Worker、變更 Cloudflare Token 權限、觸發 Production Deploy，或要求使用者把登入資訊貼在 Chat。
- 本次僅更新 AITeam 的跨 Repo 工作進度文件；CYACCweb `main` 自動部署與其他正式運作／GitHub Environment 完全未動。

### 2026-10-12 — 初始盤點與主控入口

- 已確認 AITeam/CYapps/CYapps_PVT/chihyuan-web 的 COMMON_RULES_VERSION 都是 2.8.0。AITeam 是共通規則母本，但**本文件不修改共通規則**，也不新增另一層規則。
- GitHub read-only 來源為三個管理 Repo 的 main workflow、Wrangler／部署程式、CYID／CYInvoice handoff；PVT 的 Private 內容不轉存到 AITeam Public repo。
- 尚未有 Cloudflare Dashboard 的即時 Worker/D1 全量清冊，且尚未有 GitHub Environment 實際名稱及 Secret/Variable name/scope 的直接 read-back。這部分以 P1 待辦明列，不得假裝完成。
- 使用者已明確要求：**CYACCweb main 自動部署保持不變**；不再製造任何遠端測試 Worker／probe；目標是單一正式雲端，不做粗暴刪除。
- 本次 AITeam 文件 PR 僅建立執行主控，不變更 CYID/CYInvoice/CYACC/CY Web runtime、Worker、D1、Service Binding、GitHub Secret／Variable、Deploy Workflow 或 Release。
