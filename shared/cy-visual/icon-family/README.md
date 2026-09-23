# CY Icon Family

Canonical source for the shared CY desktop application icon family.

This package is design/reference material, not a fourth governance layer.

## Files

- `ICON_FAMILY.md` — complete family specification, production workflow, AI/designer handoff, scope, and acceptance checklist.
- `CHANGELOG.md` — family-level design history.
- `reference/CY_ICON_FAMILY_CONCEPT_V1.svg` — current accepted editable concept-board reference. It is a direction board, not the precision production master.
- `reference/CY_ICON_FAMILY_CONCEPT_V1_HISTORICAL.jpg` — preserved earlier V1 visual concept for historical comparison only. It must not be treated as current geometry or production guidance.

## Production structure

Production work should use this structure as it is created:

```text
icon-family/
├─ README.md
├─ ICON_FAMILY.md
├─ CHANGELOG.md
├─ master/
│  └─ CY_ICON_MASTER.svg
├─ apps/
│  ├─ invoice/
│  ├─ accounting/
│  ├─ envelope/
│  ├─ erp-autoinput/
│  ├─ watermark/
│  ├─ converter/
│  └─ tri-invoice-calc/
└─ reference/
   ├─ CY_ICON_FAMILY_CONCEPT_V1.svg
   └─ CY_ICON_FAMILY_CONCEPT_V1_HISTORICAL.jpg
```

AITeam remains the single family-level canonical source. After a production icon is approved, the AI responsible for each application should copy only that application's approved assets into its project repository. Family-wide edits return here first.
