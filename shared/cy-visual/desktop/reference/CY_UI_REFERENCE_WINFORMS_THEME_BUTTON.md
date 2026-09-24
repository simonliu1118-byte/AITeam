# CY WinForms Theme Button Reference

> Status: `REFERENCE`  
> Validation source: CYInvoice Visual Shell V7 / Windows WinForms  
> Scope: WinForms only; this is a proven implementation reference, not a cross-framework mandate.

## Decision

For ordinary buttons, prefer the Windows / WinForms native themed `Button`.

When a key **Primary** or **Danger** action materially benefits from Theme color, limited owner-paint is acceptable when the native `Button` remains the underlying control.

Validated CY pattern:

- subclass `Button`; do not replace it with a custom `UserControl`;
- owner-paint appearance only;
- keep Click / keyboard / focus / tab / accessibility behavior on the native Button base;
- use anti-aliasing;
- clear with the parent background before drawing;
- fill and border the same rounded path;
- Hover / Pressed should change color only, not geometry;
- draw inside control bounds so anti-aliased edges are not clipped;
- if custom appearance cannot visually match the native control cleanly, fall back to the native Button.

## Validated geometry

The V7 comparison found a close visual match to current Windows themed buttons at the tested sizes using:

- fixed ~2 px corner radius for Standard / Large / Danger;
- do not increase radius merely because a button is taller;
- a small symmetric vertical paint inset (~1.5 px equivalent in the 100% reference rendering) so visible painted body height better matches the native button.

These values are **WinForms implementation reference values**, not universal CY tokens. DPI and framework scaling still require validation.

## Mixing native and themed buttons

A native Secondary button may sit beside a Theme-colored Primary or Danger button when hierarchy is intentional and geometry is visually coordinated.

Validated examples included:

- native `取消` + themed `儲存`;
- native `預覽` + large themed `開立測試發票`;
- native `關閉` + themed Danger `刪除`;
- same-label Standard and Large native-vs-themed comparisons.

Do not theme every button merely because owner-paint exists. Theme-colored owner-paint is mainly for key actions.

## Theme / Danger

- Primary uses the selected Theme Accent family.
- Danger remains an independent Danger color and never becomes Coral.
- Coral Primary may use a softer coral/pink surface so it remains visibly distinct from Danger red.

## Historical prototype source

Validated prototype branch in `CYapps`:

`design/cyinvoice-visual-shell-prototype`

Relevant historical files:

- `design/cy-desktop-visual-guide/prototypes/CYInvoiceVisualShell/RoundedThemeButtonV7.cs`
- `design/cy-desktop-visual-guide/prototypes/CYInvoiceVisualShell/ButtonLabFormV7.cs`

V7 launch commit:

`85b233a8135e0619d35f292a7805a051b43df126`
