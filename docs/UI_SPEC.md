# EXE UI specification — Alpha 2

## Interaction decisions

- Remove `新任務`.
- Remove `關閉`.
- Windows title-bar `×` is the normal close action.
- Rename `開始` to `送出`.
- Only one task may execute at a time.
- During task execution:
  - input remains editable, so the next request can be prepared;
  - `送出` is disabled and displays `執行中…`;
  - after the current task ends, `送出` becomes available again when the input is non-empty.
- If the user closes the window while a task is running, AITeam asks for confirmation before cancellation.

## Provider state presentation

Provider availability is displayed with both a colored lamp and text.

| State | Lamp | Text |
|---|---|---|
| online | green | 上線 |
| checking | yellow | 檢測中 |
| quota | orange | 超過限額 |
| authentication required | blue | 需登入 |
| temporary error | red-orange | 暫時異常 |
| error | red | 錯誤 |
| session disabled | gray | 本次停用 |
| missing CLI | dark gray | 未安裝 |

Color is never the only signal.

## Layout

The single-window left/right structure remains.

Left:
- project
- provider status cards
- task/query input
- one `送出` button

Right:
- current task
- execution progress / result log

The current PowerShell prototype is still preserved and no runtime folder migration happens in this alpha.
