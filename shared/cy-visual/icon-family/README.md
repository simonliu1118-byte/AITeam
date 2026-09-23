# CY Icon Family

Canonical source for the shared CY desktop application icon family.

This package is design/reference material, not a fourth governance layer.

## Current status

The family has moved from concept-only work into a normalized vector production-candidate stage.

Approved visual direction:

- square/full rounded-card body;
- white interior;
- large identifier above one simple app-specific motif;
- one accent family per app;
- current identifiers: `INV / ACC / ENV / Auto / WM / CVT / CAL`.

`WTM` was replaced by `WM` for CYWatermark by user decision.

## Files

- `ICON_FAMILY.md` — family specification, production workflow, handoff, scope, and acceptance checklist.
- `CHANGELOG.md` — family-level design history.
- `master/CY_ICON_MASTER.svg` — normalized 512×512 shared production geometry.
- `apps/*/*.svg` — current per-app vector production candidates.
- `reference/CY_ICON_FAMILY_CONCEPT_V1.svg` — concept-board reference; no longer the production geometry source of truth.
- `reference/CY_ICON_FAMILY_CONCEPT_V1_HISTORICAL.jpg` — preserved earlier V1 visual concept for historical comparison only.

## Production structure

```text
icon-family/
├─ README.md
├─ ICON_FAMILY.md
├─ CHANGELOG.md
├─ master/
│  └─ CY_ICON_MASTER.svg
├─ apps/
│  ├─ invoice/INV.svg
│  ├─ accounting/ACC.svg
│  ├─ envelope/ENV.svg
│  ├─ erp-autoinput/Auto.svg
│  ├─ watermark/WM.svg
│  ├─ converter/CVT.svg
│  └─ tri-invoice-calc/CAL.svg
└─ reference/
   ├─ CY_ICON_FAMILY_CONCEPT_V1.svg
   └─ CY_ICON_FAMILY_CONCEPT_V1_HISTORICAL.jpg
```

AITeam remains the single family-level canonical source. Family-wide edits return here first. After a production icon is finalized, the AI responsible for each application copies only that application's approved assets into its project repository.

PNG/ICO exports are derived assets and should be generated only from the accepted vector source, then verified under the repository binary-asset integrity rules before downstream use.
