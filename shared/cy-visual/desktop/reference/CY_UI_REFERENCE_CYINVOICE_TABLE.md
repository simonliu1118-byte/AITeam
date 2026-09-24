# CY UI Reference — CYInvoice Table / List Implementation

> Status: `REFERENCE`
>
> This is a supporting reference for the canonical CY Desktop Visual Guide. It preserves proven structure, geometry and drawing techniques from CYInvoice. It is not a separate governance layer and its historical visual values are not cross-app standards.

Reference implementation:

- `CYapps/apps/CYInvoice/src/CYInvoice.WinForms/RecordsControl.cs`
- `CYapps/apps/CYInvoice/src/CYInvoice.WinForms/NativeListViewHost.cs`

## 1. Why this reference exists

The mature CYInvoice record list demonstrates several useful desktop-table principles:

- Header and Body share continuous column geometry.
- Column separators extend cleanly from Header into Body.
- Width is recalculated when the viewport changes.
- Vertical scrollbar and DPI effects are accounted for.
- Fixed-purpose columns can coexist with one or more flexible columns.
- Header and Body do not independently guess their column positions.
- Owner-draw can control presentation while retaining native scrolling / selection behavior.

This avoids recurring defects such as 1 px Header/Body drift, last-column changes when a scrollbar appears, and DPI-induced separator mismatch.

## 2. Single Column Geometry Source

Header and Body must share one column-geometry source.

Avoid:

- Header calculating x/width independently from Body.
- Header using proportional layout while Body uses fixed values.
- Header and Body separately reconstructing right-edge positions.

If Table uses vertical separators:

> **Every Header / Body separator must form one visually continuous line. Any visible ~1 px mismatch is a UI defect.**

If a framework cannot achieve this reliably at reasonable cost, prefer a table without vertical separators over visibly misaligned grid lines.

## 3. Viewport width

Use the actual drawable/client viewport width as the column-layout input.

- A visible vertical scrollbar must not cause Header and Body to use different effective widths.
- DPI-scaled layout must use one coordinate system.
- Client borders / edge pixels must have consistent ownership.

Do not copy a fixed compensation constant across frameworks; measure with the framework’s own client-area APIs.

## 4. Column-width strategy

A useful pattern is several fixed-purpose columns plus one main flexible column.

- date, invoice number, amount, status and similar bounded fields may use reasonable baseline/minimum widths;
- name, description, note and similar variable text fields may act as flexible columns;
- when width shrinks, follow a defined shrink priority rather than compressing every column equally;
- every shrinkable column needs a readable minimum width;
- once readability would be lost, accept horizontal scrolling or a different information layout.

Historic CYInvoice numeric widths are implementation details, not canonical values. Re-measure after applying current typography, density, content and DPI.

## 5. Header / Body grid drawing

When owner-drawing:

- Header and Body separators should share the same column right-edge definition;
- border ownership must be consistent so lines do not double to 2 px;
- draw from actual cell bounds instead of estimating geometry;
- explicitly validate the final column, scrollbar edge and outer border;
- invalidate / repaint correctly after resize and scroll.

Current color, padding and historic row/header sizes are not the reference target; use the canonical Visual Guide roles/tokens.

## 6. Row height and text

Retain the result, not a framework trick:

- row height should feel natural for the chosen type size;
- text should appear vertically balanced;
- do not create excessive vertical empty space merely to enlarge rows;
- Header and Body row proportions should be coherent.

## 7. Selection, zebra, status and badge

Useful principles:

- state should be quickly legible;
- state cannot rely on color alone;
- status / badge drawing must not break grid continuity;
- badges stay inside the cell bounds and do not affect column geometry.

Historic RGB, badge radius, font size and zebra colors are not canonical values.

## 8. Alignment

- ordinary text: left;
- true numeric / money: right;
- short status / operation: may center;
- numeric-looking identifiers do not automatically right-align.

Header and Body of a column should maintain the same or semantically coherent alignment.

## 9. Resize / DPI / scroll acceptance

For grid-like tables, verify at least:

- initial open;
- maximize;
- restore;
- minimum width;
- wide layout;
- without vertical scrollbar;
- with vertical scrollbar;
- after wheel scrolling;
- 100% DPI;
- 125% DPI when that project is validating it;
- 150% DPI when that project is validating it.

Check:

1. Header/Body line continuity.
2. Last-column behavior at the scrollbar edge.
3. Flexible-column use of remaining space.
4. Unreasonable text clipping.
5. Grid line thickness jumps.
6. Selection/status/badge remaining inside the correct cell bounds.

## 10. Migration order

When adapting this pattern:

1. Preserve column geometry correctness first.
2. Apply current typography/density and remeasure row/header height.
3. Apply current theme/surface/grid/selection roles.
4. Remeasure widths and minimum widths.
5. Adjust padding / badge / status presentation.
6. Perform resize and required DPI acceptance.

Do not reuse old widths and then compensate around a new font.

## 11. One-line rule

> **Reference CYInvoice for how to keep a business table complete, precise and stable; do not copy its historical pixels and colors as the shared standard.**
