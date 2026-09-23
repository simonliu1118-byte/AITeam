# CY Shared Visual References

> Canonical source: `simonliu1118-byte/AITeam`
>
> Status: shared design/reference material. This is **not** a fourth governance layer and does not replace `REPOSITORY_RULES.md`, `REPO_POLICY.md`, or project `PROJECT_RULES.md`.

## Purpose

`shared/cy-visual/` is the canonical home for CY cross-repository visual references and production design assets.

The default model is deliberately simple:

1. Design/reference documents live here in AITeam.
2. They are not duplicated into every application repository unless there is a concrete need.
3. When a production asset is approved, each application receives only the assets that belong to that application.
4. App repositories should not independently redesign the shared family master; family-level changes return here first.

This keeps AITeam as the single visual source of truth without creating another governance layer or a large mirrored documentation tree in every repository.

## Packages

- `icon-family/` — CY Windows desktop application icon family, shared master/reference material, and later app-specific production exports.
- `desktop/` — reserved for the CY Desktop Visual Guide after that guide is explicitly promoted to the shared canonical source.

## Distribution model

For the icon family, AITeam owns the shared family source. After the full family is finalized, the AI responsible for each application should copy only that application's approved production icon assets into the target project and record the AITeam source revision in the implementation PR.

Examples:

- CYInvoice receives INV assets.
- CYAccounting receives ACC assets.
- CYEnvelope receives ENV assets.
- CYERPAutoInput receives Auto assets.
- CYWatermark receives WTM assets.
- SMARTCOPIConverter receives CVT assets.
- TriINVCalc receives CAL assets.

`DriveDownloader` is not part of the CY App Icon Family. `CYAccountingWeb` is not automatically covered by the Windows desktop icon production rules.

## Governance boundary

These files document visual direction and production references. If they conflict with a higher-priority user instruction, project rule, repository policy, or shared repository rule, the higher-priority source wins.
