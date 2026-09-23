# CY App Icon Family

> Status: `SHARED DESIGN SOURCE / VECTOR PRODUCTION CANDIDATE`
>
> Canonical repository: `simonliu1118-byte/AITeam`
>
> This document is design/reference material, not a fourth governance layer.

## 1. Purpose

The CY App Icon Family makes CY Windows desktop applications look visibly related while keeping each app easy to distinguish.

The family is unified by one square-card body, white inner field, identifier/motif hierarchy, visual mass, and complexity level. Each app keeps its own identifier, accent, and lower motif.

AITeam is the family-level canonical source. App repositories should not redesign the family independently.

## 2. Approved visual direction

The approved direction is the square/full family derived from the CYInvoice / INV visual language and the user-approved family concept board.

Core appearance:

- square canvas;
- visually full rounded-square card body;
- white interior;
- one dominant accent family per app;
- large identifier in the upper zone;
- one simple app-specific motif in the lower zone;
- clean, high-contrast Windows business-software appearance;
- no heavy shadow, 3D treatment, gloss, or decorative illustration clutter.

The old taller/narrower CYInvoice measurements remain historical reference only. They are not the production geometry target.

## 3. Production master — frozen candidate geometry

Canonical master:

`master/CY_ICON_MASTER.svg`

Current normalized geometry:

- Canvas: `512 × 512`.
- Outer body: `x=36, y=36, w=440, h=440, rx=54`.
- White inner field: `x=62, y=62, w=388, h=388, rx=39`.
- Identifier occupies the upper visual zone.
- Motif occupies the lower visual zone.
- App content may receive small optical corrections, but the body geometry does not change per app.

The current app SVGs preserve the silhouettes from the user-approved visual sources while normalizing the shared body to this master.

## 4. Current family members

| Project / App | Display family name | Identifier | Accent base | Motif |
|---|---|---|---|---|
| CYInvoice | CYInvoice | `INV` | `#006EFE` | invoice/detail lines |
| CYAccounting | CYAccounting | `ACC` | `#03A844` | accounting bars + money |
| CYEnvelope | CYEnvelope | `ENV` | `#761EE1` | envelope |
| CYERPAutoInput | CYERPAutoInput | `Auto` | `#FE6A02` | document/input + direction |
| CYWatermark | CYWatermark | `WM` | `#019EB9` | marked document / water drop |
| SMARTCOPIConverter | CYConverter | `CVT` | `#F09D03` | document conversion |
| TriINVCalc | CYTriplicateCalculator | `CAL` | `#FD4944` | calculator |

`WM` supersedes the earlier `WTM` identifier by explicit user decision.

`Auto` remains the approved readability exception to the short-uppercase default.

The two horizontal lines are an INV-specific motif, not a universal family mark.

## 5. Canonical per-app vector sources

Current vector production candidates:

- `apps/invoice/INV.svg`
- `apps/accounting/ACC.svg`
- `apps/envelope/ENV.svg`
- `apps/erp-autoinput/Auto.svg`
- `apps/watermark/WM.svg`
- `apps/converter/CVT.svg`
- `apps/tri-invoice-calc/CAL.svg`

These files are the source for later PNG/ICO export. Do not recreate production icons from prompts once an accepted SVG exists.

## 6. Color treatment

Each app uses one accent family for frame, identifier, and motif. The current vector candidates use a very small same-hue gradient to preserve the approved visual character without introducing gloss or 3D styling.

The white inner field is common across the family.

Any future color change should be reviewed side by side with the full family rather than one icon in isolation.

## 7. Small-size Windows production

Formal Windows output should include or inspect at least:

`16 / 24 / 32 / 48 / 64 / 128 / 256 px`

Do not assume a mechanical 256 px downscale is automatically final.

Allowed small-size corrections include:

- stroke thickening;
- gap simplification;
- motif simplification;
- optical centering;
- pixel snapping;
- identifier/motif balance adjustment.

Engineering previews have been generated locally at 48 / 32 / 24 / 16 px from the current SVGs. They remain derived-preview validation, not a substitute for real Windows Explorer/taskbar acceptance.

## 8. Production workflow

Use this order:

1. Start from `master/CY_ICON_MASTER.svg` and the approved app SVG.
2. Keep shared body geometry unchanged.
3. Adjust only identifier, accent, lower motif, and necessary optical corrections.
4. Review the whole family side by side.
5. Render required PNG layers.
6. Build ICO from the approved layers.
7. Verify binary byte size, dimensions/entries, and SHA-256 under repository binary-asset rules.
8. Perform real Windows acceptance where Explorer/taskbar/cache behavior matters.
9. After family approval, each app owner AI copies only that app's production assets into its project repository.

Family-level changes return to AITeam first.

## 9. Repository scope

Included:

### CYapps

- CYInvoice
- CYAccounting
- CYEnvelope
- CYERPAutoInput
- SMARTCOPIConverter
- TriINVCalc

### CYapps_pvt

- CYWatermark

Excluded unless the user explicitly changes scope:

- `DriveDownloader` — not a CY App Icon Family member.
- `CYAccountingWeb` — web project; Windows desktop icon production rules do not automatically apply.

## 10. AI / designer handoff

Before creating or revising a family icon:

- read this document;
- use `master/CY_ICON_MASTER.svg`;
- use the app's existing SVG when it already exists;
- do not invent a fresh icon language per project.

Keep unchanged:

- square family body and live area;
- corner language;
- white interior;
- identifier/motif hierarchy;
- overall visual mass and clear-space rhythm.

App-specific variables:

- identifier;
- accent;
- one simple lower motif;
- small optical corrections.

Generative images may be used for concept exploration, but accepted production assets must be normalized into the editable vector family source before downstream use.

## 11. Acceptance checklist

Before a family source is considered final:

- frame/body matches the master;
- apparent size is comparable with the other members;
- identifier block has comparable visual weight;
- identifier is optically centered;
- motif is simple, app-specific, and secondary;
- one-accent + white family system is preserved;
- no accidental extra decoration is present;
- icon remains readable at 48 px and at least one smaller size;
- family remains coherent side by side;
- derived PNG/ICO assets are traceable to the accepted SVG source;
- binary source/read-back integrity checks are complete before app integration.

## 12. References and history

- `reference/CY_ICON_FAMILY_CONCEPT_V1.svg` — concept reference; production geometry is now superseded by the master.
- `reference/CY_ICON_FAMILY_CONCEPT_V1_HISTORICAL.jpg` — preserved earlier V1 visual concept for design-history comparison only.
- Historical CYInvoice live-area measurements remain useful only for understanding the ancestor icon; they do not control the new family.

## 13. Current next step

The vector source layer is now present in AITeam. The next production step is to export verified PNG/ICO layers from these SVGs, perform small-size/native Windows checks, and only then distribute each app's approved assets to its owning repository.
