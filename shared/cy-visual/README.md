# CY Shared Visual References

> Canonical source: `simonliu1118-byte/AITeam`
>
> Status: shared design/reference material adopted through the normal governance chain. This package is **not** a fourth governance layer and does not replace `REPOSITORY_RULES.md`, `REPO_POLICY.md`, or project `PROJECT_RULES.md`.

## Purpose

`shared/cy-visual/` is the canonical home for CY cross-repository visual references and production design assets.

The model is deliberately simple:

1. Shared visual sources live in AITeam.
2. Repositories that adopt a shared visual source declare that adoption in their normal `REPO_POLICY.md` / `PROJECT_RULES.md` chain.
3. Full shared design documents are not mirrored into every application repository.
4. App repositories must not independently redesign a shared family / visual standard; shared changes return to AITeam first.
5. App-specific permanent exceptions belong in that app's sole `PROJECT_RULES.md`.

This keeps AITeam as the single visual source of truth without creating a fourth governance layer.

## Canonical packages

- `desktop/` — **canonical CY Desktop Visual Guide**, including Phase 1 shared UI direction and mature implementation references.
- `icon-family/` — **canonical CY Windows desktop application icon family**, shared master plus app-specific production SVG / PNG / ICO assets.

## Desktop Visual Guide

Canonical guide:

`desktop/CY_DESKTOP_VISUAL_GUIDE.md`

The guide defines the shared CY desktop visual language, including:

- Modern Business Desktop / native-first direction;
- typography and density hierarchy;
- Blue / Teal / Coral / Apricot themes;
- surface, spacing and alignment roles;
- button, input, tabs, table/list, dialog and shell guidance;
- accessibility baseline;
- Grid Continuity requirement;
- Phase 1 / Phase 2 boundary;
- link to the canonical icon-family package.

100% / 96 DPI integrated shell is accepted. 125% / 150% remains deferred and must not be described as validated until real acceptance exists.

## Icon-family distribution model

AITeam owns the family source. Each application should copy only its own approved production icon assets into the target project and record the AITeam source revision in the implementation PR.

- CYInvoice → INV
- CYAccounting → ACC
- CYEnvelope → ENV
- CYERPAutoInput → Auto
- CYWatermark → WM
- SMARTCOPIConverter → CVT
- TriINVCalc → CAL

`DriveDownloader` is not part of the CY App Icon Family. `CYAccountingWeb` is not automatically covered by the Windows desktop icon-production rules.

## Governance boundary

These files document the canonical visual source. Mandatory adoption is expressed by the governing repository / project rules, not by creating another rules layer.

If a project-specific permanent exception is required, place it in that project's sole `PROJECT_RULES.md`. Higher-priority user instructions and existing governance precedence continue to apply.
