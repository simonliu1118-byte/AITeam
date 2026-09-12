# AITeam Change Pipeline Redesign — Spec (draft, pending approval)

This document is a design spec, not yet implemented. It consolidates the logic/flow
review discussed with the project owner and the decisions made in that discussion.
Nothing in this file changes behavior until it is implemented and merged version by
version (see "建議的版本拆分" at the end).

Status: **草案，待確認**。確認沒問題後才會開始拆版本實作。

## 設計原則（貫穿全文件）

1. **AITeam 永遠只有三個 AI 角色可用：GPT/Codex、Claude、Gemini/Antigravity。** 任何設計不得假設未來會有第四個 AI。目前所有角色分派（Scout / Plan Gate / Implementer / Challenger / Final Reviewer / Repairer）都必須從這三個裡面挑，最多同時存在 3 個不同身分。
2. **人在迴圈中（human-in-the-loop）要在兩個關鍵時間點出現**：計畫定案前（Plan Gate 討論）、正式合併前（依風險決定要不要人工確認）。中間的 Scout / Implement / Challenge / Final Review / Repair 維持全自動。
3. **正確性驗證盡量交給既有、可信的機制**，而不是讓 AITeam 自己重新發明一套：build/test 驗證交給目標專案自己的 GitHub CI，而不是 AITeam 自己執行測試指令。
4. 既有的安全機制（worktree 隔離、`git diff --check`、推送前重新比對 remote SHA、atomic 操作、失敗保留工作區供檢查、不覆蓋既有 tag、不 force push）全部保留，不因這次改版而放寬。

---

## 一、Inquiry（查詢）流程的修正

**現況問題**：Change 任務一開始一定會 `SafeSyncAsync` 跟 GitHub 同步，但 Inquiry 任務完全不會，導致查詢可能基於過時的本機狀態回答。

**修正**：`InquiryService.RunAsync` 在建立唯讀查詢 worktree 之前，先呼叫 `SafeSyncAsync`（沿用 Change 任務同一套安全同步邏輯：本機有未提交變更就拒絕、本機領先 GitHub 就拒絕、本機落後就 fast-forward）。查詢本身不寫入任何東西，這個同步只是確保「回答的當下，本機跟 GitHub 認知一致」。

---

## 二、Change 任務整體流程（新版）

```
使用者送出請求
  └─ 單一 AI 判斷 Inquiry / Change（不變）
       └─ 若為 Change：
            1. SafeSyncAsync + 建立隔離 worktree（不變）
            2. Scout（擴充：順便回報技術棧與是否已有 CI）
            3. Plan Gate ⇄ 使用者討論迴圈（新，見第三節）
               → 定案後，重新 SafeSyncAsync + 重新鎖定 baseSha
            4. Implementer 實作（擴充：若專案沒 CI，這輪一併補建）
            5. Verify（git diff --check + 有變更；不變）
            6. Challenge ⇄ Final Review ⇄ Repair 迴圈（擴充：見第四節）
            7. Final Reviewer 依最終 diff 重新確認版號等級
            8. 版號 bump + commit
            9. 推送任務分支 → 開 PR（不再直接推正式分支，見第五節）
            10. 等待目標專案 CI 結果
            11. 依風險等級決定：自動合併 or 等待人工確認合併（見第五節）
            12. 合併完成後，在正式分支的合併 commit 上打 tag、推 tag、同步本機
```

---

## 三、Plan Gate ⇄ 使用者討論迴圈

### 狀態

Plan Gate 每次回覆用一個狀態行開頭：

- `AITeamPlanStatus: NEEDS_INPUT` + 具體問題（可以不只一個問題）
- `AITeamPlanStatus: READY` + 完整計畫（含 `AITeamRisk` / `AITeamVersionBump` 兩行，格式維持現有設計）

### 迴圈規則

- **沒有輪數上限。** 只要 Plan Gate 認為還有不確定的地方，就可以一直問。
- 使用者每次可以：
  1. 回答問題（文字輸入，餵回 Plan Gate 重新產生回覆）
  2. 按下「交給 AI 全權判斷、直接定案」——這是唯一的強制終止路徑，觸發後 Plan Gate 這次呼叫「必須」輸出 `READY`，不能再問
- 當 Plan Gate **自己**輸出 `READY`（不是被使用者強制的），**不會直接進入實作**，而是先把完整計畫呈現給使用者，附上「還有沒有要補充？」：
  - 使用者確認「沒有，開始執行」→ 正式定案
  - 使用者輸入補充內容 → 這段補充連同原本的討論記錄一起餵回 Plan Gate，Plan Gate 重新判斷（可能維持 `READY` 並更新計畫內容、也可能因為補充帶出新的不確定性而變回 `NEEDS_INPUT`）——這個確認關卡本身可以重複發生，一樣沒有次數上限
  - 使用者也可以在這一關直接按「交給 AI 全權判斷」跳過確認，效果同上面的強制終止路徑

流程圖：

```
[Plan Gate 呼叫]
   │
   ├─ NEEDS_INPUT ──► 顯示問題給使用者 ──► 使用者回答 ──┐
   │                                                    │
   │        使用者按「交給AI決定」──────────────────────┤
   │                                                    │
   └─ READY ──► 顯示計畫，問「還有要補充嗎？」          │
                  │                                     │
                  ├─ 確認「開始執行」──► 【真正定案】    │
                  ├─ 補充內容 ─────────────────────────►│（迴到 Plan Gate 呼叫）
                  └─ 按「交給AI決定」──► 【真正定案】    │
                                                          │
                  【真正定案】 ◄──────────────────────────┘
                       │
                       ▼
       重新 SafeSyncAsync、重新鎖定 baseSha
                       │
                       ▼
                  進入 Implementer
```

### 技術影響

- `ChangeTaskService.RunAsync` 需要新增一個委派參數（例如 `Func<PlanQuestion, CancellationToken, Task<PlanAnswer>> askUser`），由 `MainWindow` 提供實作：跳出/顯示問答區塊，等待使用者輸入或點按鈕後才回傳。
- `MainWindow` 需要新增一個「計畫討論」UI 區塊（在「目前任務」卡片內，或獨立一塊），顯示 Plan Gate 的問題/計畫內容、輸入框、「交給 AI 決定」按鈕。
- worktree 在整個討論期間維持存在（本地分支不佔用遠端資源，沒有時間壓力）。
- `baseSha` 的鎖定時機從「任務一開始」延後到「計畫真正定案的那一刻」，確保不管討論拖多久，後面的推送race check 都是跟「定案當下」的 GitHub 狀態比較，不會因為討論期間 GitHub 有新 commit 就白白失敗。

---

## 四、Challenge / Final Review / Repair 迴圈的擴充

### 4.1 兩個 AI 上線時的降級模式（不再直接擋下任務）

- `PickDifferent` 選 Final Reviewer 時，優先排除「實作者 + Challenger」兩者都不行的組合；只有在真的只剩 2 個 AI 上線、找不到第三個獨立角色時，才允許 Final Reviewer 退回跟 Challenger 是同一個 AI，並設定 `DegradedReview = true`。
- `DegradedReview = true` 時：
  - 每一輪 log 明顯標註：「⚠️ 僅 2 個 AI 上線，Challenge 與 Final Review 為同一 AI，獨立性下降」
  - `ChangeTaskResult` 新增 `bool DegradedReview` 欄位，讓**最終完成訊息**（不只是會捲動的 log）永久顯示這個警告
  - Final Reviewer 的 prompt 額外加一句：「你剛才已經以 Challenger 身分寫過意見，現在切換成獨立審查者角色，對自己剛才的意見保持懷疑，找出可能遺漏或過寬鬆之處」
  - 用於「合併方式」判斷的**有效風險等級**自動提升一級（LOW→NORMAL；NORMAL→HIGH 是否要做，先列為可選項，預設先只做 LOW→NORMAL 這一級）——因為獨立審查被削弱本身就是風險因子

### 4.2 Challenger / Final Reviewer 角色輪替（3 個 AI 都在線時）

- 目前設計是整個任務固定用同一組 Challenger / Final Reviewer。改成：從 Repair 第 2 輪（round ≥ 1）起，讓上一輪的 Challenger 和 Final Reviewer **互換身分**。這樣「第二意見」是真的來自不同角度重新看，而不是同一個審查者一直重複審自己說過的話。
- 只在 3 個 AI 都在線時做輪替；只有 2 個在線（降級模式）時無從輪替，維持 4.1 的設計。

### 4.3 Repair 申訴機制（減少「假警報逼你硬改」的狀況）

- Repairer 的輸出格式新增一個選項：`AITeamRepairStance: DISPUTE` + 理由，用在「認為 Challenge / Final Review 的意見是誤判」的情況，取代目前「不管對不對都得改程式碼」的唯一路徑。
- 有 `DISPUTE` 時：這一輪放寬 `VerifyWorkingTreeAsync`「一定要有 git 變更」的要求；把 Repairer 的反駁理由重新餵給 Final Reviewer，讓它在知道對方不同意的情況下再裁決一次。
  - Final Reviewer 仍堅持 `REPAIR` → 反駁失敗，Repairer 下一輪必須真的動手改，正常計入輪數
  - Final Reviewer 改判 `PASS` → 反駁成立，流程照常往下走（版號重新確認 → commit → 開 PR）

### 4.4 Final Review 依「實際 diff」重新確認版號等級

- `BuildFinalReviewPrompt` 新增一行要求輸出：`AITeamVersionBumpConfirm: PATCH|MINOR|MAJOR`——這是 Final Reviewer 看著最終 diff 給的版號判斷，可能跟 Plan Gate 實作前的猜測不同。
- `BumpVersionAsync` 改吃這個值，而不是 Plan Gate 階段的舊值——版號等級用「實際做了什麼」決定，而不是「原本以為要做什麼」。

### 4.5 版號檔案異動的說明（不新增審查輪，只加說明）

- `BuildFinalReviewPrompt` 加一句：「你核准後，AITeam 會額外對版本檔案做一次單純的版號數字更新，這不需要你事先審查。」——讓 Final Reviewer 知情，避免多花一輪 LLM 呼叫去審一個機械式的單行文字變更。

### 4.6 Repair 使用舊 Scout 證據的提醒

- `BuildRepairPrompt` 加一句：「下面的 Scout 報告與計畫是任務一開始產生的，程式碼在你之前的實作/修正後已經改變，動手前請重新確認目前檔案的實際內容，不要完全信任這段舊描述。」——不重新跑 Scout（成本高、Repairer 本來就能自己讀檔案），只用提示降低誤信舊資料的風險。

---

## 五、正式化流程改版：PR + 目標專案 CI + 依風險決定合併方式

### 5.1 流程

1. Final Review PASS（含 4.3 的申訴成立路徑）後，版號 bump（用 4.4 的值）、commit
2. **不再直接 `git push --atomic` 到正式分支**，改成：
   - 推送任務分支 `aiteam/task-<id>` 到 origin
   - 用 `gh pr create` 開一個 PR：`aiteam/task-<id>` → 專案預設分支，內文放計畫摘要、風險等級（含是否為降級審查）、Implementer / Challenger / Final Reviewer 名單
3. 輪詢這個 PR 的 CI 檢查結果（`gh pr checks`）
4. **CI 失敗** → 把失敗訊息（log 摘要）餵給 Repairer 修，重跑 Verify，重新推同一個任務分支（PR 會自動更新），重新等待 CI；設一個重試輪數上限（沿用 `max_repair_rounds` 的精神，用同一個設定或另開一個 `max_ci_repair_rounds`），一直不過就保留 PR 開著，明確告知使用者，不強行合併
5. **CI 通過**：
   - **LOW / NORMAL risk**（含 4.1 降級調整後的有效風險）→ AITeam 自動合併 PR（`gh pr merge`，合併策略見 5.3）
   - **HIGH risk** → PR 留著不合併，AITeam 明確提示「需要人工確認合併：{PR連結}」，然後輪詢 PR 是否已被合併（不論是使用者自己在 GitHub 上按合併，還是之後 AITeam GUI 加一個「確認合併」按鈕呼叫同一支 API，兩者等價）
6. **合併完成後**，在正式分支的合併 commit 上打 tag、`git push` tag、同步本機 `SafeSyncAsync`

### 5.2 目標專案沒有 CI 時：補建，而不是跳過

- Scout 階段偵測 `.github/workflows/` 是否存在任何 workflow 檔案，並回報偵測到的語言/建置工具（dotnet/csproj、node/package.json、python 等）
- 沒偵測到 CI 時，Plan Gate 的計畫**自動加入一項交付內容**：「同時新增一份最小可用的 GitHub Actions workflow（build + test），對應偵測到的技術棧」——這是併入同一個任務、同一個 PR 完成，不是另開任務、不需要新角色
- Challenge / Final Review 照常審查這個 PR 的完整 diff，新增的 CI 檔案自然包含在內
- **首次補建 CI 的這個 PR 本身**：因為 AITeam 一律用同 repo 內分支（非 fork）開 PR，GitHub 對 PR 自己新增的 workflow 通常會直接在這個 PR 上觸發，所以多數情況下這個 PR 本身也能拿到真正的 CI 結果。保底規則：如果輪詢一段寬限時間後這個 PR 完全沒有出現任何 check（少數 repo 權限限制的情況），**只有這一次**允許跳過 CI 閘門、只靠 Final Review 把關；下一個任務開始，CI 已經存在，就正常照 5.1 走，不會一直跳過

### 5.3 需要確認的技術細節

- 用 `gh` CLI 操作 PR/合併/查 CI 狀態（跟現有「都是呼叫外部 CLI」的架構一致，不用讓 AITeam 自己保管 GitHub token，吃使用者機器上已經 `gh auth login` 的憑證）
- 合併策略（merge commit / squash / rebase）建議做成 `ProjectEntry` 的欄位（類似 `VersionFile`/`TagPrefix`），預設一般 merge commit

---

## 六、風險等級（Risk）目前的實際作用

改版後，Risk（連同 `DegradedReview` 調整過的「有效風險」）**唯一**會實際影響管線行為的地方：**決定第五節的合併方式**（LOW/NORMAL 自動合併、HIGH 等人工確認）。除此之外，Risk 純粹是顯示用途（log、最終摘要）。

以下是討論過但**不在這次改版做**、先記錄成 backlog 的想法：

- HIGH risk 強制觸發 Plan Gate 的使用者問答（即使 Plan Gate 自己不覺得需要問）
- HIGH risk 允許更多 Repair 輪數（正確性更重要，可以多試幾次）
- Risk 決定各角色使用的模型/reasoning effort 設定（LOW 用較快/便宜設定、HIGH 用最高規格）
- LOW risk 跳過 Scout，直接進 Plan Gate（省一輪 LLM 呼叫）

~~HIGH risk 要求 2 個獨立 Challenger 才進 Final Review~~ —— **已確認不可行並移除**：這需要「實作者 + 2 個 Challenger + 1 個 Final Reviewer」共 4 個 AI 角色同時存在，AITeam 只有 3 個 AI，永遠湊不出來。

---

## 七、資料結構 / 設定變更清單（概念層級，非程式碼）

- `ChangeTaskResult` 新增：`DegradedReview: bool`、PR 連結（合併前）、實際使用的版號確認來源
- `ProjectEntry` 新增：合併策略設定（5.3）
- `RuntimeConfigService` / `settings.json` 新增：CI 等待輪詢間隔與逾時、CI 失敗重試上限、（可選）`max_task_minutes` 總時間上限
- `ChangeTaskService.RunAsync` 簽章新增：`askUser` 委派參數
- Plan Gate / Challenge / Final Review / Repair 的 prompt 內容都需要對應調整（狀態行格式、新增欄位、新增提醒文字）

---

## 八、對 UI 的影響總覽

- `MainWindow` 新增「計畫討論」互動區塊（問答 + 「交給 AI 決定」按鈕）
- 「目前任務」/ log 需要能顯示：目前是否為降級審查、PR 連結、是否在等待人工確認合併
- （原本第 10 點提過的「結構化階段進度」與「任務總時間上限」維持是獨立的中優先級項目，不在這次改版範圍內，但會用到同一批 UI 擴充位置，之後可以一起做）

---

## 建議的版本拆分（實作順序）

依相依性與風險排列，之後逐一開 PR：

1. **Inquiry 同步修正**（第一節）——最小、獨立、低風險
2. **兩個 AI 降級模式 + 角色輪替**（4.1、4.2）——不動到主流程結構，低風險
3. **Final Review 版號重新確認 + 版號檔案異動說明**（4.4、4.5）——小改動
4. **Repair 舊證據提醒 + 申訴機制**（4.6、4.3）——中等
5. **Plan Gate ⇄ 使用者討論迴圈**（第三節）——UI + 簽章都要動，中高風險，需要你確認合併時機（我先自己驗證過關會直接合併，還是這種牽涉 UX 決策的想跟你先過一次再合併？可以到時候再問）
6. **PR + CI + 依風險合併**（第五節）——影響最大的架構調整，建議再拆成「先開 PR + 等 CI」跟「補建 CI」兩個子版本

每個版本照舊：低/中風險驗證過關我直接合併，架構性或牽涉重大行為改變的先跟你確認過再合併。
