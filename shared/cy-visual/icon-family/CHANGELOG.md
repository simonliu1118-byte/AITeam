# CY Icon Family Changelog

## Unreleased

- Consolidated family documentation into `README.md`, `ICON_FAMILY.md`, and `CHANGELOG.md`.
- Removed the standalone creation brief and standalone version file.
- Clarified that AITeam is the single family-level canonical source; downstream app repositories should receive only their approved production assets.
- Preserved the earlier V1 visual concept as `reference/CY_ICON_FAMILY_CONCEPT_V1_HISTORICAL.jpg` for historical comparison only.
- User approved the current square/full visual family direction as the target for formal production.
- Added normalized `master/CY_ICON_MASTER.svg` with a 512×512 shared production geometry.
- Added vector production candidates for `INV / ACC / ENV / Auto / WM / CVT / CAL` under `apps/`.
- Changed CYWatermark identifier from `WTM` to `WM` by explicit user decision.
- Updated the editable concept reference to show `WM` and to defer production geometry to the master/vector sources.
- Locally rendered 48 / 32 / 24 / 16 px engineering previews from the vector candidates; real Windows acceptance remains a later derived-asset step.

## 0.1.0 — 2026-09-23

Initial shared canonical package.

- Established AITeam as the upstream source for CY cross-repository icon-family references.
- Adopted the new square-family direction.
- Defined shared frame/body geometry, upper identifier zone, lower app-specific motif zone, color strategy, optical consistency, and small-size production principles.
- Defined short uppercase identifiers as the default.
- Recorded `Auto` as the approved readability exception for `CYERPAutoInput`.
- Clarified that the two horizontal lines are specific to `INV`, not a universal family element.
- Recorded current family scope across `CYapps` and `CYapps_pvt`.
- Explicitly excluded `DriveDownloader` from the CY App Icon Family.
- Added a future-conversation / AI creation brief and reusable generation template.
- Documented that generative images are concept material; final repeatability should come from a shared editable vector master.
