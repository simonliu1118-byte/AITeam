# codebase-memory-mcp 評估與 AITeam 導入備忘

> 狀態：**暫緩導入（Deferred）**  
> 評估日期：2026-09-28  
> 上游專案：`DeusData/codebase-memory-mcp`  
> 本次檢查基準：上游正式版 `v0.11.0`（2026-09-15）

## 1. 目的

本文件記錄對 `codebase-memory-mcp` 的初步技術與安全審查，以及未來評估是否導入 AITeam 的條件。

目前決策不是拒絕此方案，而是：

- **現在先不導入 AITeam 正式流程。**
- 等上游 Windows 穩定性、更新流程、敏感檔案控制與文件一致性更成熟後再重新評估。
- 未來若導入，定位為 **AITeam 的可選 code-intelligence / knowledge-graph layer**，不是 Git、GitHub、治理文件或原始碼的替代品。

## 2. 工具定位

`codebase-memory-mcp` 會把 repository 解析成持久化的程式知識圖譜，再透過 MCP 提供 AI coding agent 查詢。

主要價值包括：

- repository architecture overview；
- function / class / module graph search；
- caller / callee trace；
- Git diff impact analysis；
- code snippet retrieval；
- cross-service / cross-repository 關聯；
- persistent local index；
- 減少 agent 每次重新掃描大量 source 的成本。

AITeam 若未來使用，預期可改善：

1. 新 task 進入時的 repository comprehension。
2. 大型專案的 dependency / call-path 探索。
3. change blast-radius 判斷。
4. 多 session 重複讀取同一批 source 所產生的 token 與 tool-call 成本。

## 3. 授權

上游採 MIT License。

授權面沒有阻礙 AITeam：可使用、修改、fork、內部整合與商用，但若散布衍生內容，仍需依 MIT 要求保留相關 copyright / license notice。

目前沒有必要 fork。上游更新速度快，過早維護私有 fork 反而會增加安全與同步成本。

## 4. 本次安全審查結論

### 4.1 未發現明顯惡意行為跡象

本次實際檢查了：

- Windows `install.ps1`；
- `SECURITY.md`；
- release / checksum / build provenance 設計；
- MCP 與 daemon 部分 source；
- agent hooks / config 寫入行為；
- `.env` / environment config 掃描邏輯；
- security fuzz / injection 測試；
- 近期 Windows、OOM、update、indexing 相關 issue。

截至本次審查，**沒有發現刻意植入後門、竊取 source、竊取 token、下載不明 payload 或明顯 telemetry/exfiltration 邏輯的證據**。

這不等同於宣告軟體「無漏洞」。此工具本質上需要深度讀取 repository、建立本機 index、修改 coding-agent 設定並啟動背景程序，因此屬於高信任面工具。

## 5. 正面安全特性

目前上游在 OSS MCP 工具中具備相對完整的安全措施，包括：

- release SHA-256 checksum；
- SLSA Build Level 3 provenance；
- Sigstore / cosign；
- SBOM；
- VirusTotal release scanning；
- CodeQL；
- fuzz testing；
- shell-injection adversarial tests；
- path traversal tests；
- SQLite authorizer 防止不必要的 `ATTACH` / `DETACH`；
- project-root containment；
- release archive member allowlist；
- Windows installer 對 staging ACL、ZIP path、reparse point、checksum 等做額外防護。

Windows installer 並非單純「下載 EXE 後直接執行」，其防禦性設計明顯高於一般小型 GitHub 工具。

## 6. 目前主要風險 / 暫緩原因

### 6.1 敏感檔案與 `.env` 可進入 MCP 可見範圍

這是目前最重要的導入風險。

上游 `pass_envscan.c` 會掃描：

- `.env`；
- `.env.*`；
- Dockerfile；
- YAML；
- TOML；
- Terraform；
- shell config；
- `.properties` / `.cfg` / `.ini` 等。

程式有 secret filtering，也會略過部分典型 credential/key 檔名，但這不能視為完整 secret boundary。

上游 issue #1079 亦記錄 `.env` File node 的內容可透過 `get_code_snippet` 取得。

因此真正的風險未必是：

`codebase-memory-mcp -> 上游作者伺服器`

而可能是：

`敏感檔案 -> codebase-memory-mcp -> MCP -> coding agent -> AI provider`

AITeam 未來若導入，不得假設 `.gitignore` 或「本機處理」等同秘密不會進入模型上下文。

### 6.2 Agent config / hooks 修改範圍大

完整安裝會偵測不同 coding clients，並可能寫入：

- MCP config；
- `AGENTS.md` / instructions；
- skills；
- lifecycle hooks；
- Claude Code `SessionStart` / `SubagentStart` / `PreToolUse` / `PostToolUse` 等 integration。

部分 hooks 會觀察 `Grep` / `Glob` / `Bash` / `Read` 並注入 graph context。

這些功能本身符合產品定位，但表示一旦上游 release、installer 或 integration 被污染，影響面會比單純 CLI 搜尋工具大。

AITeam 未來若導入，**不得第一步就讓 installer 自動改全部 agent 設定**。

### 6.3 供應鏈風險仍存在

上游 release security 相對完整，但官方 Quick Start 仍會從 `main` 下載 installer。

即使 release ZIP 與 `checksums.txt` 做 SHA-256 對照，也只能證明 ZIP 與該 checksum 一致；如果 release account / workflow / release set 同時被攻破，攻擊者仍可能發布惡意檔案及其正確 checksum。

未來 AITeam 導入時應：

- pin 明確 release tag；
- 不從 `main` / 不受控 `latest` 直接執行 installer；
- 驗 SHA-256；
- 驗 GitHub/SLSA attestation；
- 必要時驗 Sigstore bundle；
- 保持 Defender / endpoint protection 啟用。

### 6.4 Update 流程仍有破壞性 bug 紀錄

上游 issue #2200（本次審查時仍 open / high priority）記錄：

- non-interactive `update -y` 在某些情況先刪除既有 project indexes；
- 之後 update 又因 variant selection 失敗；
- 最終 binary 未更新，但 indexes 已全部刪除。

這不會直接刪除 Git source，但可能造成大量 index 重建成本與 CI / agent 工作中斷。

因此未來不得讓 agent 自主執行 unattended update。

### 6.5 Windows 記憶體 / 穩定性仍需觀察

上游 issue #2184 記錄 Windows 16 GB RAM 環境索引大型 repository 時發生 OOM，並影響其他程式。

`v0.11.0` 已重新處理 memory budget / spill / worker isolation，方向正確，但 AITeam 本身也會同時啟動多個 engineering agents，兩者資源競爭需要實機驗證。

AITeam 導入前至少要測：

- 16 GB Windows；
- 多 agent 同時存在；
- 大 repository；
- watcher 開 / 關；
- indexing peak RAM；
- cache size；
- agent latency；
- crash / OOM 時是否安全 fail closed。

### 6.6 文件與 source 曾出現行為不一致

本次審查時：

- `SECURITY.md` 仍描述 MCP initialize 後會背景查 GitHub Releases API；
- `v0.11.0` source 的 daemon implementation 已明確移除 production update-check provider，目的就是避免每個 agent session 主動 phone home。

目前較像文件同步落後，而非暗藏 telemetry，但安全相關文件與實際 binary 行為不一致仍是治理扣分項。

未來導入前應再次交叉驗證：README / SECURITY / source / release notes 是否一致。

## 7. AITeam 未來正確定位

若未來導入，系統邊界應維持：

```text
Git / GitHub
    = source of truth

REPOSITORY_RULES / REPO_POLICY / PROJECT_RULES
    = governance source of truth

codebase-memory-mcp
    = derived local code-intelligence index

AITeam engineering agents
    = 使用 graph 輔助探索、規劃、impact analysis
```

不得讓 graph database 取代：

- source code；
- Git history；
- governance；
- tests；
- build verification；
- human / agent final review。

Graph result 只能視為 derived evidence；實際修改前仍需讀取相關 source，重大結論仍需用 source / tests / build 驗證。

## 8. 建議的 AITeam 整合模式

未來若通過評估，優先考慮：

```text
AITeam Scout / Planner
        |
        +--> Git / files
        |
        +--> codebase-memory-mcp (read-oriented graph queries)
        |
        +--> governance docs
```

第一階段不要讓 CBM：

- 自動 update；
- 自動改全部 provider 設定；
- 自動安裝 hooks；
- 掃描整個磁碟或過大的 allowed root；
- 主動索引含真實 secrets 的 repository；
- 成為唯一的 change-impact 判斷來源。

## 9. 未來重新評估條件

至少確認以下條件後，再討論正式導入：

1. Windows 近期高優先級 OOM / indexing stability 問題已有可信修正與實際使用回饋。
2. Update 不再有「先刪 index、後失敗」類 destructive ordering 問題，或 AITeam 有安全 wrapper 完全避開該流程。
3. 有更明確的 file / path exclusion 機制，可可靠排除 `.env`、credentials、key、runtime config 等敏感檔。
4. Agent integration 可採 explicit opt-in，且 config / hooks 變更可預覽、可回復、可限定 provider。
5. Runtime network behavior 與 SECURITY / README / source 描述一致。
6. Release attestation / checksum 驗證流程可自動化但不降低驗證強度。
7. 在 AITeam 目標 Windows 機器實測資源消耗可接受。
8. 實際 benchmark 證明對 AITeam 的 repository comprehension / token / tool-call 確有明顯效益。

## 10. 未來安全 PoC 流程

正式接 AITeam 前，先做隔離 PoC：

1. Pin 一個明確 stable release。
2. 手動下載 release，不直接執行 `main` installer。
3. 驗 SHA-256 + SLSA/GitHub attestation。
4. 非 Administrator 執行。
5. 第一次使用 binary-only / `--skip-config` 類模式。
6. `CBM_ALLOWED_ROOT` 只允許單一測試 repository。
7. `CBM_CACHE_DIR` 使用獨立 ASCII 路徑。
8. 關閉 auto-index / auto-watch / watcher。
9. 不開 UI。
10. 測試 repo 不放任何真實 secret。
11. 放置假的 canary secret，驗證 MCP 是否能讀取或回傳它，以實際界定資料邊界。
12. 監控 process tree、RAM、CPU、disk、network egress。
13. 確認正常後只接一個 coding client。
14. hooks 與自動 agent config 最後再單獨評估，必要時永久不用。
15. Update 由 AITeam 受控流程處理，禁止 agent 自行 unattended update。
16. Index 永遠視為可重建 cache，不得存放 AITeam 唯一狀態。

## 11. 目前決策

**2026-09-28：暫不導入 AITeam 正式 pipeline。**

原因不是已發現惡意行為，而是目前：

- Windows 上游仍快速演進；
- 有近期高優先級 indexing / update 問題；
- `.env` / secret boundary 對 AITeam 尚不夠乾淨；
- agent config / hook integration 的 trust surface 偏大；
- AITeam 本身已有多 agent、worktree、governance、Git 安全流程，不適合把一個仍快速變動的高權限 MCP 直接插入正式鏈路。

後續等上游更穩、更成熟，再重新審查最新 release，若條件通過，再以受控 PoC 方式導入 AITeam。

## 12. 上游參考

- Repository: https://github.com/DeusData/codebase-memory-mcp
- Security Policy: https://github.com/DeusData/codebase-memory-mcp/security
- Releases: https://github.com/DeusData/codebase-memory-mcp/releases
- Issue #1079 (`.env` / graph exposure): https://github.com/DeusData/codebase-memory-mcp/issues/1079
- Issue #2184 (Windows indexing / OOM): https://github.com/DeusData/codebase-memory-mcp/issues/2184
- Issue #2200 (update destructive ordering): https://github.com/DeusData/codebase-memory-mcp/issues/2200

---

本文件是技術評估／導入備忘，不建立新的永久治理規則。正式導入時仍須遵守 AITeam 根目錄 `REPOSITORY_RULES.md`、`REPO_POLICY.md`、`PROJECT_RULES.md` 與當次使用者明確指示。
