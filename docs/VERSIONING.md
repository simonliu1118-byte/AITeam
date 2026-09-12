# AITeam Versioning — Reference

AITeam 的正式版本規則改由 repository 根目錄 `REPOSITORY_RULES.md` 第 6 節統一管理，AITeam-specific 例外則寫在根 `PROJECT_RULES.md`。

目前採用 `X.Y.Z + Build N`：

- Major (`X`)：只有使用者可以決定升級。
- Minor (`Y`)：AI 可在明顯功能階段／具份量功能升級時判斷升級。
- Patch (`Z`)：一般新的修改工作項目。
- `Build N`：同一工作項目尚未完成而再次返修；不是 GitHub workflow run number。

本檔只作快速參考，不是永久規則來源；若與正式規則不同，以根三層規則為準。
