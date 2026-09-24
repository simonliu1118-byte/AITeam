# CY Desktop Visual Guide

> Canonical source: `simonliu1118-byte/AITeam/main/shared/cy-visual/desktop/CY_DESKTOP_VISUAL_GUIDE.md`
>
> Status: `CANONICAL / PHASE 1 APPROVED`
>
> This document is the shared CY desktop visual source of truth. Its mandatory force comes from the governing repository policy / project rules that adopt this canonical source; it does not create a fourth governance layer.

**Checkpoint:** 2026-09-24  
**100% / 96 DPI Final Integrated Shell:** user accepted  
**125% / 150% DPI:** deferred validation; not yet manually accepted  
**Next design phase:** Phase 2 — Layout / Interaction / Workflow / Keyboard / IA

---

# 0. Status model

- **`CORE`** — shared CY visual requirement. Projects that adopt this guide should follow it unless an explicit project exception exists.
- **`RECOMMENDED RANGE`** — preferred range, not a pixel lock.
- **`APP CHOICE`** — project / screen semantics decide; AI or developer should recommend the simplest suitable option.
- **`REFERENCE`** — proven implementation or measured example, not a cross-framework mandate.
- **`DEFERRED`** — deliberately not a release-blocking requirement yet.
- **`PHASE 2`** — intentionally deferred to interaction / workflow / IA work.
- **`HISTORICAL`** — retained only as historical evidence, not a current target.

## 0.1 Phase 1 summary

| Area | Status | Canonical direction |
|---|---|---|
| Visual identity | `CORE` | Modern Business Desktop; clean, low-decoration, desktop-first, medium/high density allowed |
| Rule strength | `CORE` | Core / Recommended Range / App Choice; avoid unnecessary hard-coded cross-app dimensions |
| Font | `CORE` | `Microsoft JhengHei UI`; fallback `Microsoft JhengHei → Segoe UI → system sans-serif` |
| Typography hierarchy | `CORE` | shared role hierarchy; exact pt can vary by app |
| WinForms standard input | `REFERENCE` | at 96 DPI / 100%, 10 pt + natural control height is a good general reference |
| Density | `APP CHOICE` | Compact / Standard / Comfortable are descriptive, not fixed size packages |
| Theme | `CORE` | Blue / Teal / Coral / Apricot; no Warm Theme |
| Surface | `CORE` | Continuous / Sectioned / Carded; Card only for truly independent units |
| Button | `CORE` | Native-first; mature owner-paint allowed for important Primary / Danger actions |
| Input / Combo / Date | `CORE` | Native-first; do not force single-line control height for visual symmetry |
| Tabs | `CORE / APP CHOICE` | Header-only Custom preferred when higher CY consistency is useful; Native Tab acceptable |
| Table / List | `CORE / APP CHOICE` | Native-first; neutral header; Grid Continuity is mandatory; selection follows data semantics |
| Dialog / MessageBox | `CORE — ADVISORY` | use native MessageBox when it fully expresses the interaction |
| Accessibility | `CORE` | keyboard focus must remain perceivable; state cannot rely on color alone |
| Window shell | `CORE — ADVISORY` | Minimal / Business / Workbench are reference patterns, not templates |
| Icon family | `CORE` | use AITeam canonical icon-family package; no independent app-level redesign |
| 100% / 96 DPI | `ACCEPTED` | integrated Phase 1 shell accepted |
| 125% / 150% DPI | `DEFERRED` | do not claim accepted until actually checked |
| UX / Workflow | `PHASE 2` | not frozen by Phase 1 |

---

# 1. Purpose and visual position — `CORE`

CY desktop applications span WinForms, Qt / PySide6, native Win32 / Go and other Windows frameworks. The goal is a shared visual skeleton, not pixel-perfect identical controls across frameworks.

Shared goals:

- applications should visibly belong to the same CY desktop product family;
- preserve desktop-business efficiency, readability and information density;
- prefer stable framework / OS behavior over decorative custom drawing;
- prioritize Traditional Chinese, English, numbers, tables and data-entry readability;
- reduce design decision cost without increasing product instability.

Visual keywords:

- **Modern Business Desktop** — not a web-dashboard skin.
- **Clean but Dense** — clear hierarchy while allowing dense operational screens.
- **Low Decoration** — no design-by-shadow / gradient / colored-card overload.
- **Text-first** — text labels first, icon support second.
- **Readable** — clarity over novelty.
- **Framework-flexible** — shared roles and hierarchy matter more than identical widget internals.

---

# 2. Strength and override model

## 2.1 Core

Shared core includes:

- theme roles and state-color semantics;
- typography hierarchy;
- surface hierarchy;
- accessibility baseline;
- table Grid Continuity;
- native-first control principle;
- icon-family canonical source.

## 2.2 Recommended Range

Use ranges where exact numbers legitimately depend on framework, DPI, data density or screen role.

## 2.3 App Choice

The following are normally app / screen choices:

- density;
- surface model;
- record-row vs cell selection;
- row hover;
- native tab vs Header-only Custom;
- whether Primary / Danger needs themed owner-paint;
- zebra rows, cards, sidebars and similar optional devices.

## 2.4 App override

A permanent visual exception must be narrow and intentional. It should be documented in the adopting project’s `PROJECT_RULES.md` when it is truly a lasting project exception. An override must not become a second design system.

---

# 3. Typography and density

## 3.1 Font — `CORE`

Primary font:

`Microsoft JhengHei UI`

Fallback:

`Microsoft JhengHei` → `Segoe UI` → system sans-serif

Weight guidance:

- Regular — body, input, table body.
- Medium / Semibold — section title, table header, active tab, ordinary emphasis.
- Bold — limited use for major title or exceptional summary; do not bold the entire UI.

## 3.2 Typography roles — `CORE / RECOMMENDED RANGE`

| Role | Recommended range |
|---|---:|
| Major / App Title | 16–20 pt |
| Page Title | 13.5–16 pt |
| Section Title | 11.5–13 pt |
| Body / Field | 9.5–11 pt |
| Secondary | 8.5–10 pt |
| Button | 9–11 pt |
| Table Header | 9–10.5 pt |
| Table Body | 9–10.5 pt |
| Badge / Status | 8.5–10 pt |

Hierarchy:

`Major Title > Page Title > Section Title > Body > Secondary`

Secondary text should primarily step down through color / weight rather than becoming too small to read.

### WinForms standard input — `REFERENCE`

At Windows 96 DPI / 100%:

- `Microsoft JhengHei UI 10 pt` has validated Traditional Chinese readability;
- native TextBox / ComboBox / DateTimePicker do not need forced identical configured heights;
- respect framework natural / preferred height and align with layout / baseline instead.

## 3.3 Density — `APP CHOICE`

`Compact / Standard / Comfortable` are descriptive categories only.

- high-density screens may use smaller type, fields and spacing;
- normal screens use Standard rhythm;
- preview / result / low-information screens may be more spacious;
- one screen should not mix obviously different densities without a functional reason;
- do not distort native controls merely to satisfy a density label;
- dialogs may use a different density from the main window if internally consistent.

## 3.4 DPI — `CORE PRINCIPLE / DEFERRED VALIDATION`

Priority:

1. OS DPI awareness.
2. Framework native scaling.
3. Automatic / adaptive layout.
4. Necessary override only as a last resort.

Do not build an extra scaling engine or multiply scale factors twice.

Check for:

- TextBox / Button text clipping;
- Label overflow;
- Tab header clipping;
- table header/body geometry continuity;
- dialog breakage;
- scrollbar-induced layout changes.

Validation status:

- 100% / 96 DPI — accepted.
- 125% / 150% — deferred; not yet accepted.

---

# 4. Color and theme

## 4.1 Core neutral roles — `CORE`

| Token | Reference | Use |
|---|---|---|
| `Neutral.White` | `#FFFFFF` | control / table / raised |
| `Neutral.Window` | `#F8FAFC` | window background |
| `Neutral.Subtle` | `#F8FAFC` | secondary surface |
| `Neutral.ReadOnly` | `#F1F3F5` | read-only / disabled surface |
| `Neutral.Border` | `#D1D5DB` | ordinary border |
| `Neutral.Divider` | `#E5E8EC` | divider |
| `Neutral.Grid` | `#DDE1E6` | table grid |
| `Text.Primary` | `#1F2937` | primary text |
| `Text.Secondary` | `#667085` | secondary text |
| `Text.Disabled` | `#98A2B3` | disabled text |
| `Text.OnAccent` | `#FFFFFF` | text on accent |

Avoid proliferating many almost-identical gray tokens.

## 4.2 Theme set — `CORE`

Theme changes the accent family, not the typography / neutral / control system.

### Blue

`Accent #2563EB` · `Hover #1D4ED8` · `Pressed #1E40AF` · `Soft #DBEAFE` · `Focus #60A5FA` · `Selection #DBEAFE`

### Teal

`Accent #0D9488` · `Hover #0D8076` · `Pressed #0F766E` · `Soft #D1F2EB` · `Focus #2DD4BF` · `Selection #CCFBF1`

### Coral

`Accent #D4657B` · `Hover #C4566E` · `Pressed #AD485E` · `Soft #FCE8ED` · `Focus #E39BAC` · `Selection #F7DCE3`

Coral must remain visibly separate from Apricot and Danger red.

### Apricot

`Accent #D8844A` · `Hover #C3733F` · `Pressed #AA6435` · `Soft #FAECDD` · `Focus #E0A178` · `Selection #F6E3D1`

There is no Warm Theme in Phase 1.

## 4.3 State colors — `CORE`

State semantics remain independent of Theme.

| State | Reference | Soft |
|---|---|---|
| Success | `#21825C` | `#EAF5F0` |
| Warning | `#A66B10` | `#FAF1E3` |
| Danger | `#B43737` | `#F8EAEA` |
| Info | `#356A9A` | `#EAF1F7` |

**Danger stays Danger and never becomes Coral.**

Accent is primarily for Primary, focus, active tab, selection and small highlights. Do not flood headers / groups / windows with accent color.

Phase 1 is Light Mode first. Dark Mode is not defined here.

---

# 5. Surface

## 5.1 Surface roles — `CORE`

- `Surface.Window`
- `Surface.Workspace`
- `Surface.Section` — may inherit / be transparent
- `Surface.Control`
- `Surface.Raised`
- `Surface.Subtle`
- `Surface.Border`
- `Surface.Divider`

## 5.2 Surface models — `APP CHOICE`

### Continuous

Best for dense data entry, invoice and accounting screens. Sections normally inherit the workspace; controls / tables may be white.

### Sectioned

Best for ordinary desktop tools, left/right work areas and preview screens. Main work regions are clear without nesting cards everywhere.

### Carded

Use only for genuinely independent workflow units, drop zones, previews or result summaries.

Raised / Card surfaces should normally be white + ~1 px border without a default shadow.

---

# 6. Spacing and alignment

## 6.1 Spacing — `CORE / RECOMMENDED RANGE`

Preferred spacing scale:

`4 / 8 / 12 / 16 / 24 / 32 px`

This is not a pixel lock; DPI / framework approximation is allowed.

Typical page margins:

- high density: 12–16 px;
- ordinary desktop tool: 16–24 px;
- preview / low density: 24–32 px.

## 6.2 Alignment — `CORE`

- keep Label and Input columns aligned within a section;
- same-row controls should have coherent visible height / text baseline;
- section spacing > field spacing;
- resize must preserve alignment grid;
- related fields should retain reasonable common boundaries;
- table Header / Body geometry must remain continuous.

Multi-column forms may maintain separate label columns; do not force one unnecessarily wide page-wide label column.

---

# 7. Core components

## 7.1 Button — `CORE`

- ordinary Secondary / utility actions: native OS / framework Button first;
- do not reimplement ordinary button behavior for small radius differences;
- no ordinary pill buttons; pills are primarily for badges;
- width is content-driven: text + reasonable padding;
- Primary may use Accent solid + white text;
- Danger remains separate from Theme;
- Native Secondary and themed Primary / Danger may coexist when geometry and hierarchy are coherent;
- do not theme every button merely because owner-paint is available.

Size reference:

| Type | Height | Text |
|---|---:|---:|
| Compact | 30–32 px | 9–9.5 pt |
| Standard | 34–38 px | 9.5–10.5 pt |
| Large | 42–48 px | 10.5–12 pt |

Large is mainly for strong main-workspace primary actions, not normal dialog footers.

For proven WinForms owner-paint guidance, read `reference/CY_UI_REFERENCE_WINFORMS_THEME_BUTTON.md`.

## 7.2 TextBox / Numeric / Label — `CORE`

- do not force single-line TextBox height merely for uniform frames;
- if native control cannot truly vertically center text, use natural height rather than a custom shell;
- align actual visible height and text baseline, not only configured Height;
- read-only / disabled may use subtle gray but must remain readable;
- focus must not change geometry;
- error: Danger border + nearby short message; no full-cell red / glow / layout jump;
- placeholder does not replace Label.

Numeric:

- true amounts / quantities / prices / percentages normally right-align;
- identifier-like numeric strings (postal code, tax ID, invoice number) do not automatically right-align.

## 7.3 ComboBox / DatePicker — `CORE`

- same visual family / row alignment as TextBox;
- retain native dropdown arrow / calendar popup;
- do not wrap controls in a custom shell merely for radius / arrow styling;
- align using natural control height and layout.

## 7.4 Tabs — `CORE / APP CHOICE`

Shared appearance:

- low visual presence;
- active state uses stronger weight; custom option may add ~2 px accent underline;
- inactive state uses neutral text;
- no large pill / hero navigation.

### Preferred: Header-only Custom

When stronger CY consistency is valuable:

- customize only the header;
- keep existing page / TabPage / UserControl contents and lifecycle;
- do not redraw the whole page;
- preserve focus routing, parenting and page state;
- preserve keyboard page switching such as `Ctrl+Tab` / `Ctrl+Shift+Tab`.

### Alternative: Native Tab

Native Tab is acceptable. Traditional selected-tab bevel / frame is not by itself a defect.

Do not show the old black dotted focus rectangle, but keyboard focus **must not disappear**. Replace it with a low-presence but perceivable focus state when needed.

WinForms `FlatButtons` is not a CY Tab direction.

## 7.5 Section / Group / Divider / Card — `CORE`

- Section normally inherit / transparent;
- Section Title: roughly 11.5–13 pt, Semibold, primary text;
- Divider: ~1 px low contrast;
- stable GroupBox may remain;
- Card only for preview / result / drop area / independent workflow unit.

---

# 8. Table / ListView / DataGrid — `CORE / APP CHOICE`

Native-first. A full custom-drawn table is not required merely to meet the CY visual language.

## 8.1 Density

| Type | Row height reference |
|---|---:|
| High density | ~26–29 px |
| Standard | ~30–34 px |
| Comfortable | ~35–40 px |

## 8.2 Visual

- header: subtle neutral surface + Semibold text;
- header remains neutral; current-cell selection must not turn an entire column header into a deep Accent block;
- sort state should be subtle;
- body usually white;
- zebra optional and very light;
- grid low contrast;
- selection: Accent.Selection soft tint + dark text.

## 8.3 Alignment

- text / name / address: left;
- money / numeric values: right;
- short status: may center;
- identifiers: align by semantics rather than character class.

## 8.4 Selection — `APP CHOICE`

- Record List: Full Row Select is usually appropriate.
- Editable / Cell Grid: Cell Select is usually appropriate.
- Row Hover: optional; do not require extra event/style complexity solely for uniformity.

## 8.5 Grid Continuity — `CORE / MUST`

Header and Body must share one column-geometry source. A visible ~1 px separator mismatch is a UI defect.

Account for:

- resize;
- maximize / restore;
- scrollbar appearance / disappearance;
- control border / viewport width;
- DPI scaling;
- owner-draw border ownership.

If a framework cannot reliably maintain aligned vertical separators, remove the vertical lines rather than shipping visibly mismatched grid lines.

Mature implementation reference: `reference/CY_UI_REFERENCE_CYINVOICE_TABLE.md`.

---

# 9. Secondary controls and supporting states

- Checkbox / Radio: native-first, body font, vertically aligned, no oversized web-style controls.
- Toggle: only for true immediate On/Off; use Checkbox when there is no mature toggle implementation.
- Progress: determinate progress for known progress; native spinner / marquee for unknown time; text outside the progress bar.
- Scrollbar: native-first; account for its effect on table / layout geometry.
- Menu / Context Menu: native-first; low-presence hover; Danger item may use Danger text without a full red row.
- TreeView / Sidebar / Navigation List: no default dark sidebar; active item may use Accent.Soft + stronger text / thin indicator; do not wrap every item as a pill/card.
- Splitter: thin visual divider; hit area may be wider than the drawn line.
- Empty / No Data / Drop Area: concise copy and small icon; no giant illustration; drop area may use subtle dashed border and Accent.Soft on drag hover.
- Toolbar / ToolStrip: inherit Button / Icon / Divider system; function ordering belongs to Phase 2.

---

# 10. Accessibility baseline — `CORE`

- keyboard focus must remain perceivable;
- removing the traditional focus rectangle requires another visible focus state;
- Error / Success / Warning / status cannot be conveyed by color alone;
- disabled / secondary text must remain readable;
- do not intentionally break native High Contrast / keyboard / accessibility behavior through custom drawing.

---

# 11. Dialog / MessageBox / Status

## 11.1 Dialog shell — `CORE — ADVISORY`

- native title bar / window behavior first;
- CY consistency applies to content typography / surface / inputs / buttons;
- do not convert every dialog into borderless / custom chrome.

## 11.2 Native MessageBox — `CORE — ADVISORY`

When a simple message, warning, OK, Yes/No, question or overwrite confirmation is completely expressible by native MessageBox, use it directly.

Do not replace a simple MessageBox with a custom form merely to apply Theme color.

## 11.3 Custom Dialog

Use a custom dialog when MessageBox cannot reasonably carry the interaction, such as:

- forms / password input / settings;
- preview;
- multi-selection;
- large details / expandable technical detail;
- wizard.

## 11.4 Button position — `APP CHOICE`

Right-bottom action group is a common business-desktop default, but not a fixed rule. Centered or other layouts are acceptable when the dialog content supports them.

Keep:

- natural button width;
- consistent spacing;
- clear Primary / Secondary / Danger semantics.

## 11.5 Status / Validation

- Banner: soft state surface + text/icon; no large saturated red/green blocks.
- Badge: pill is acceptable; small and low-saturation; not a button.
- Inline Validation: Danger border + nearby short message; no layout jump.

---

# 12. Main window shell — `CORE — ADVISORY`

- native Windows title bar first;
- title bar normally shows App Icon + App Name only;
- Internal Header is optional and should add persistent information value;
- Internal Header must not merely repeat App Name;
- Header prefers white / neutral / Accent.Soft rather than a full deep-accent band;
- Footer / Status Bar exists only for meaningful persistent status;
- avoid stacking Title Bar + Header + Toolbar + Tabs + Page Title + Section Title without clear need.

Reference patterns:

- **Minimal** — Title Bar + Content.
- **Business** — Title Bar + Tabs / Toolbar + Content + optional Status.
- **Workbench** — Title Bar + optional compact Header / Toolbar + Main Workspace + optional Status.

These are patterns, not templates.

---

# 13. App Icon Family — `CORE`

Desktop visual work must use the canonical family package:

`AITeam/main/shared/cy-visual/icon-family/`

Current production family:

- INV — CYInvoice
- ACC — CYAccounting
- ENV — CYEnvelope
- Auto — CYERPAutoInput
- WM — CYWatermark
- CVT — SMARTCOPIConverter
- CAL — TriINVCalc

Current icon family principles:

- shared square/full rounded-card master;
- white interior;
- one dominant accent per app;
- large identifier above one simple app-specific motif;
- flat / high-contrast / modern-business appearance;
- no 3D, gloss, heavy shadow or decorative illustration clutter;
- `Auto` is the approved readability exception to short-uppercase identifiers;
- INV’s two horizontal lines are INV-specific;
- family-level edits return to AITeam first;
- app repos receive only their own approved production assets.

Historical old INV occupancy measurements are not current geometry targets. The canonical production master / SVG / PNG / ICO in `shared/cy-visual/icon-family/` controls current output.

---

# 14. Cross-framework and complexity guardrails — `CORE`

- role consistency > identical low-level widget implementation;
- do not replace stable framework controls when native controls are sufficiently close;
- do not introduce fragile custom drawing for radius, arrow, scrollbar or ordinary 1–2 px differences;
- mature limited owner-paint is allowed where it materially improves Primary / Danger or Header-only Custom tabs;
- owner-paint should normally replace appearance only, not native behavior;
- normal DPI / font-rendering / OS-theme differences are acceptable;
- do not change framework merely to satisfy this guide;
- do not require every app to implement every Theme or Surface Model;
- do not require a dedicated component library for every component;
- validate on representative app / shell before broad rollout;
- prioritize roles, proportions and visual result over hard-coded x/y/padding;
- permanent app override is a narrow exception, not a redesign.

**Exception:** Table Grid Continuity is not a trivial 1–2 px preference; visible grid mismatch remains a defect.

---

# 15. Validation and phase boundary

## 15.1 Phase 1 accepted scope

Phase 1 covers:

- color / theme;
- surface;
- typography / density;
- spacing / alignment;
- button / input / combo / date;
- tabs;
- table / list;
- secondary controls;
- dialog / MessageBox / status;
- shell visual pattern;
- icon-family canonical boundary;
- 100% / 96 DPI WinForms integrated validation.

Do not keep expanding Phase 1 merely because another control might exist.

## 15.2 Phase 2 — `PHASE 2`

Phase 1 intentionally does not freeze:

- functional workflow;
- exact control placement;
- Primary Action final location;
- toolbar order;
- tab order and page IA;
- Enter / Esc / Tab behavior;
- dialog trigger timing;
- form field order;
- search / query flow;
- workflow step count;
- shortcuts;
- major navigation / IA redesign.

---

# 16. Canonical maintenance and downstream use

- AITeam `main` is the single canonical source for this guide and the CY icon family.
- Do not maintain a second full visual guide in CYapps or CYapps_pvt.
- Repositories that adopt this guide declare that adoption in their governing `REPO_POLICY.md` or `PROJECT_RULES.md`.
- A permanent project exception belongs in the project’s sole `PROJECT_RULES.md`.
- Design/reference files remain supporting material; mandatory authority comes from the three-layer governance chain that points to them.
- Family-wide / guide-wide changes return to AITeam first.
- 125% / 150% DPI remains deferred until actual manual validation exists; never rewrite this as passed without evidence.

Reference material:

- `reference/CY_UI_REFERENCE_CYINVOICE_TABLE.md` — proven table/grid geometry principles.
- `reference/CY_UI_REFERENCE_WINFORMS_THEME_BUTTON.md` — proven WinForms limited owner-paint button pattern.
- `reference/CY_UI_REFERENCE_ICON_FAMILY_GEOMETRY_HISTORICAL.md` — old INV measurement context only; current icon-family master supersedes its geometry.
