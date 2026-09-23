# CY App Icon Family

> Status: `SHARED DESIGN SOURCE / PRODUCTION ASSETS READY`
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

## 5. Canonical production assets

Canonical per-app vector sources:

- `apps/invoice/INV.svg`
- `apps/accounting/ACC.svg`
- `apps/envelope/ENV.svg`
- `apps/erp-autoinput/Auto.svg`
- `apps/watermark/WM.svg`
- `apps/converter/CVT.svg`
- `apps/tri-invoice-calc/CAL.svg`

Each app directory also contains:

- `png/16.png`
- `png/24.png`
- `png/32.png`
- `png/48.png`
- `png/64.png`
- `png/128.png`
- `png/256.png`
- `<IDENTIFIER>.ico`

`ASSET_MANIFEST.json` records source and derived byte sizes, SHA-256 values, Git blob SHA-1 values, PNG dimensions/modes, and ICO entry sizes. `tools/export_icons.py` is the reproducible derivation path.

Do not recreate production icons from prompts once an accepted SVG exists.

## 6. Color treatment

Each app uses one accent family for frame, identifier, and motif. The current vectors use a very small same-hue gradient to preserve the approved visual character without introducing gloss or 3D styling.

The white inner field is common across the family.

Any future color change should be reviewed side by side with the full family rather than one icon in isolation.

## 7. Small-size Windows production

Formal Windows output includes native layers at:

`16 / 24 / 32 / 48 / 64 / 128 / 256 px`

The current exporter renders every PNG directly from the canonical SVG at its target size; it does not mechanically generate the whole set from one 256 px raster. The ICO is then constructed from those exact seven PNG payloads.

The exporter verifies:

- each PNG has the expected dimensions;
- each ICO exposes all seven expected native sizes;
- source and derived byte size / SHA-256 values;
- Git blob SHA-1 values used for repository read-back verification.

Real Windows Explorer/taskbar appearance remains a separate integration acceptance step because shell cache and native presentation cannot be fully represented by repository CI.

## 8. Production workflow

Use this order:

1. Start from `master/CY_ICON_MASTER.svg` and the approved app SVG.
2. Keep shared body geometry unchanged.
3. Adjust only identifier, accent, lower motif, and necessary optical corrections.
4. Review the whole family side by side.
5. Run `tools/export_icons.py` to render native PNG layers and build the ICO.
6. Verify `ASSET_MANIFEST.json` and repository Git blob read-back.
7. Perform real Windows acceptance where Explorer/taskbar/cache behavior matters.
8. After family approval, each app owner AI copies only that app's production assets into its project repository.

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
- PNG/ICO assets are traceable to the accepted SVG source;
- binary source/read-back integrity checks are complete before app integration;
- real Windows shell appearance is checked when the app integrates the asset.

## 12. References and history

- `reference/CY_ICON_FAMILY_CONCEPT_V1.svg` — concept reference; production geometry is superseded by the master.
- `reference/CY_ICON_FAMILY_CONCEPT_V1_HISTORICAL.jpg` — preserved earlier V1 visual concept for design-history comparison only.
- Historical CYInvoice live-area measurements remain useful only for understanding the ancestor icon; they do not control the new family.

## 13. Current next step

The canonical vector source and reproducible PNG/ICO production assets are now present in AITeam and have repository read-back identifiers recorded. The remaining acceptance work is real Windows Explorer/taskbar inspection during downstream app integration, followed by copying only the relevant app's approved assets into its owning repository.
