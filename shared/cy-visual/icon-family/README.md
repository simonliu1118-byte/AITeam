# CY Icon Family

This folder is the canonical source for the CY Windows desktop application icon family.

## Read first

- `ICON_FAMILY.md` — the complete family specification, production workflow, AI handoff guidance, and acceptance checklist.
- `CHANGELOG.md` — family-level design history.
- `reference/` — concept/reference material that is useful for comparison but is not the precision production master.

## Production layout

As the family is finalized, use:

```text
icon-family/
├─ README.md
├─ ICON_FAMILY.md
├─ CHANGELOG.md
├─ reference/
├─ master/
│  └─ CY_ICON_MASTER.svg
└─ apps/
   ├─ invoice/
   ├─ accounting/
   ├─ envelope/
   ├─ erp-autoinput/
   ├─ watermark/
   ├─ converter/
   └─ tri-invoice-calc/
```

`master/` and `apps/` are created only when real production assets exist; empty placeholder directories are not needed.

## Source-of-truth rule

Family-level changes are made here first. Application repositories receive only their approved app-specific production assets after the family is finalized. Do not maintain independent copies of the family specification in CYapps or CYapps_pvt unless a concrete future workflow requires it.
