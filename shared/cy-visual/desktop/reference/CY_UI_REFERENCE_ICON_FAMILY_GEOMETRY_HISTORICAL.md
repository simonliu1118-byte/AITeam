# CYInvoice Legacy Icon Geometry — Historical Reference

> Status: `HISTORICAL / SOURCE MEASUREMENT ONLY`  
> Source: historical `CYapps/apps/CYInvoice/assets/CYInvoice.ico`  
> **This document is not the current CY Icon Family geometry standard.**

## 1. Purpose

The original CYInvoice `INV` icon was the visual ancestor used while the CY icon family was being explored. Its geometry was measured to distinguish apparent visual-size differences from canvas-size differences.

That measurement remains useful as historical evidence, but current production direction is controlled by:

`AITeam/main/shared/cy-visual/icon-family/`

The current family uses a newer square/full master. The old INV silhouette is not a geometry target for ACC / ENV / Auto / WM / CVT / CAL or future members.

## 2. Historical measurement

The old production `CYInvoice.ico` contained native layers at:

`16 / 24 / 32 / 48 / 64 / 128 / 256 px`

Historical measured artwork bounds:

| Canvas | INV visual Live Area | Width occupancy | Height occupancy | Near-opaque core |
|---:|---:|---:|---:|---:|
| 16 × 16 | ~13 × 14 | ~81.2% | ~87.5% | ~12 × 12 |
| 24 × 24 | ~20 × 21 | ~83.3% | ~87.5% | ~18 × 20 |
| 32 × 32 | ~26 × 28 | ~81.2% | ~87.5% | ~24 × 27 |
| 48 × 48 | ~39 × 42 | ~81.2% | ~87.5% | ~38 × 41 |
| 64 × 64 | ~52 × 55 | ~81.2% | ~85.9% | ~50 × 55 |
| 128 × 128 | ~103 × 112 | ~80.5% | ~87.5% | ~101 × 110 |
| 256 × 256 | ~206 × 222 | ~80.5% | ~86.7% | ~206 × 222 |

Historical summary:

- width occupancy ~80–83%;
- height occupancy ~86–88%;
- 48 px visual area ~39 × 42 px.

These values describe **what the old INV icon was**, not what current CY icons must be.

## 3. Terminology retained

Useful concepts retained from the measurement work:

- **Canvas** — complete bitmap / ICO layer.
- **Live Area** — visible artwork bounds inside the canvas.
- **Opaque Core** — near-fully-opaque interior reference.
- **Optical Margin** — apparent empty space around artwork.
- **Identifier Zone** — upper recognition area.
- **Motif Zone** — lower supporting area containing one simple app-specific motif.

## 4. Principles that remain valid

- canvas size alone does not guarantee equal apparent size;
- compare family members side by side for visual mass;
- optical centering matters more than mathematically identical margins;
- small ICO layers may need optical adjustment instead of blind downscaling;
- identifier and motif hierarchy should remain consistent;
- production should inspect native layers actually shipped.

Not retained:

- old INV width/height occupancy as a current target;
- old INV narrow/tall silhouette as master geometry;
- the assumption that new icons must fit the old INV frame proportion.

## 5. Current source of truth

Use:

`AITeam/main/shared/cy-visual/icon-family/`

Current production family includes `INV / ACC / ENV / Auto / WM / CVT / CAL`, with a shared editable square-family master and verified SVG / PNG / ICO assets.

When revising a CY icon:

1. Read the canonical AITeam icon-family package.
2. Treat this file only as historical measurement context.
3. Never copy old INV live-area percentages as new geometry requirements.
4. Build from the current square-family master.
