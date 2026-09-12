# AITeam Project Rules

本文件是 AITeam 產品本身唯一的專案永久規則來源。共通規則依根 `REPOSITORY_RULES.md`，repo-specific 規則依根 `REPO_POLICY.md`。舊 `docs/TEAM_RULES.md`、`docs/VERSIONING.md` 與其他 docs 只能作說明、歷史或設計參考，不得建立另一套永久規則。

## 1. 產品定位

- AITeam 是 Windows 本機 AI 協作／開發流程工具，負責任務判斷、repository 分析、隔離 worktree、實作、審查、修復、版本與 Git 操作。
- `src/AITeam.App` 是目前正式實作；`prototype/` 僅保留歷史參考，不得在無明確理由下恢復成正式 operational path。
- AITeam 自己的規則不得覆蓋被管理 repository 的正式治理規則。處理 CYapps / CYapps_pvt 或其他已有規則的 repo 時，先讀目標 repo 的 `REPOSITORY_RULES.md`、`REPO_POLICY.md`、目標 `PROJECT_RULES.md`。

## 2. 任務類型與修改權限

- 使用者先描述需求，AITeam 必須先判斷 inquiry 或 change；意圖不清楚時預設為 inquiry，不得把模糊需求視為修改授權。
- Inquiry 為唯讀：不建立寫入 worktree、不修改 source、不升版、不 commit、不 merge、不 tag、不 push、不 Release。
- Confirmed change task 使用隔離 branch/worktree；不得直接修改已註冊 repository 的 main working tree。
- 所有寫入工作都必須限制在目標 project/repo 範圍，不得順手 refactor 無關程式。

## 3. AI 角色與基本 pipeline

預設角色：

- Gemini / Antigravity：Scout、證據蒐集、draft plan、獨立 challenge。
- GPT / Codex：Plan Gate、風險判斷、最終 technical review／acceptance。
- Claude Code：主要 implementer 與 repairer。

Confirmed change 預設 pipeline：

1. Scout + evidence + draft plan。
2. GPT/Codex 核對證據、修正計畫、決定正式風險等級。
3. Claude Code implementation。
4. Tests / build / verification。
5. Gemini / Antigravity independent challenge。
6. GPT/Codex final review。
7. 必要時 repair loop，再重新驗證。

風險等級可改變檢查深度，但不得把未執行的檢查宣稱為通過。

## 4. Provider 可用性與 fallback

- GPT/Codex、Claude、Gemini/Antigravity 各自有獨立可用狀態。
- quota / usage / rate limit 可進入 cooldown；authentication failure 需另行記錄。
- 一般 CLI、format、provider、暫時 network failure 不得永久停用 provider，應留下診斷紀錄並依 fallback 處理。
- Read-only stage 若發生修改視為 safety stop，不是正常 fallback。
- degraded mode 可以繼續分析／修復，但正式 Merge/Tag/Push 至少應有兩個不同 engineering agents，且最終 reviewer 不得與 implementer 為同一角色；若目標 repo 的規則要求更嚴格，以目標 repo 為準。
- 手動 provider deselection 僅限當次 session；除 quota／usage／authentication 類持續狀態外，不應跨重啟永久保存一般錯誤停用狀態。
- Provider 診斷 log 位於 runtime log 目錄，不得提交 Git。

## 5. Worktree / Git 安全

- 實作使用 `D:\AITeam\worktrees` 下的隔離 worktree；失敗時可保留該 task/worktree 供診斷，不得為清理方便破壞 main。
- Force-push、history rewrite、remote branch/tag deletion、Release publishing、deployment 等高影響操作不得因一般 change task 自動執行；若目標 repo 規則或使用者當次明確授權允許，才可依該規則處理。
- Git history / GitHub 是永久 source of truth；AI chat/session state 不是 source control。
- 專案 registration 與 repository storage 分離；移除 registration 不得刪除 source、Git history 或 remote repo。

## 6. 版本規則

- AITeam 自身版本遵守根 `REPOSITORY_RULES.md` 的 `X.Y.Z + Build N` 制度。
- 目前 AITeam 的 `VERSION` 為基礎版號；根 `BUILD` 保存同一工作項目返修次數。
- AITeam 在處理其他 managed repository 時，**必須使用目標 repo 自己的版本規則**，不得把 AITeam 舊有「每完成一個 task 就一定 bump/tag」邏輯硬套到所有專案。
- 只有使用者可決定 Major；Minor 可由負責 AI 依共通規則判斷；Patch/Build 依共通規則區分新工作與同工作返修。

## 7. Commit / Merge / Tag / Push / Release

- Inquiry 不執行上述寫入動作。
- Change task 的 commit、merge、tag、push 必須同時符合本文件與目標 repository 規則；目標 repo 若要求 PR、人工 Release、Build 身分確認或禁止自動 tag，AITeam 必須遵守。
- 不得把 AITeam 內部 pipeline 的方便性當成越權理由。
- **Change task 完成合併後，AITeam 不自動建立 git tag，也不自動建立／發布 GitHub Release。** 版本檔（VERSION／BUILD）照常在合併前更新，但「正式版」這個身分（tag + Release）一律等使用者另外明確觸發才建立，不論目標專案是 AITeam 自己還是其他 managed repository。
- GitHub Release publishing 與 deployment 預設不是一般 change task 的自動步驟；只有目標 repo 規則或使用者明確授權時才執行。

## 8. 驗證與失敗處理

- failed tests/builds 不得報告為 passed。
- 缺少非必要本機工具時標記 skipped / unavailable，不得寫成 passed。
- repair loop 到達設定上限後停止，自動保留可診斷狀態並回報，不得無限重試。
- 任何成功 merge/push 前都必須依目標 repo 規則完成必要驗證。

## 9. 機密與 runtime

- 不得把 password、API key、OAuth token、cookies、production credential、客戶 runtime data 或其他秘密放入 prompt、log、commit、task artifact 或 Public AITeam source。
- `D:\AITeam\logs`、`config`、`tasks`、`repos`、`worktrees` 等 machine-specific runtime 不提交 AITeam repo。
- AITeam 若必須操作目標 repo 的秘密，只能依目標 repo 已核准的安全機制處理，不得複製到共通規則、AITeam source 或模型提示內容。
