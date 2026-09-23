# CY App Icon Family

> Status: `REFERENCE / SHARED DESIGN SOURCE`
>
> Canonical repository: `simonliu1118-byte/AITeam`
>
> This document is design/reference material, not a fourth governance layer.

## 1. Purpose

The CY App Icon Family makes CY Windows desktop applications look visibly related while keeping each app easy to distinguish.

The family is unified by a shared **square-card geometry, visual mass, identifier hierarchy, frame language, and complexity level**. It is not unified by forcing every app to use the same lower symbol.

The production model is based on **one editable vector family master**. New app icons should be derived from that master rather than independently generated from prompts.

## 2. Current direction

The accepted direction is a **new square, visually fuller family master** derived from the CYInvoice / INV visual language.

The current production CYInvoice icon is the historical visual ancestor, but its slightly taller/narrower measured geometry is **not** the future family geometry target. The new master should preserve the successful INV character while normalizing the family into a more square shared body.

Core appearance:

- square canvas;
- visually square rounded-card body;
- white interior;
- one dominant accent color per app;
- colored rounded-square frame;
- large identifier in the upper zone;
- one simple app-specific motif in the lower zone;
- flat, clean, high-contrast Windows business-software appearance;
- no heavy shadow, gloss, 3D treatment, or decorative illustration clutter.

The existing concept board is stored at:

`reference/CY_ICON_FAMILY_CONCEPT_V1.svg`

It is a visual reference, not the precision production master.

## 3. Family skeleton — CORE

### 3.1 Outer body

All family members should share closely matched:

- body proportion;
- frame thickness;
- corner-radius language;
- white inner field;
- apparent live area;
- outer/inner clear-space balance;
- overall visual mass.

When displayed side by side, one app must not look conspicuously smaller, narrower, taller, heavier, or more edge-to-edge than the others.

### 3.2 Identifier zone

Default rule:

> Use a short uppercase abbreviation whenever it remains reasonably recognizable.

Current identifiers:

| App | Identifier |
|---|---|
| CYInvoice | `INV` |
| CYAccounting | `ACC` |
| CYEnvelope | `ENV` |
| CYERPAutoInput | `Auto` — approved readability exception |
| CYWatermark | `WTM` |
| SMARTCOPIConverter | `CVT` |
| TriINVCalc | `CAL` |

Different letter shapes may receive small optical corrections. Comparable apparent size matters more than identical font-size numbers.

`Auto` changes only the identifier strategy. It does not permit a different frame, body proportion, motif scale, or family weight.

### 3.3 Motif zone

The lower zone contains **one simple app-specific motif**.

| App | Motif direction |
|---|---|
| INV | invoice/detail lines |
| ACC | accounting/chart/money |
| ENV | envelope |
| Auto | automated input / document + direction |
| WTM | watermark / marked document |
| CVT | conversion / document transform |
| CAL | calculator |

The two horizontal lines are an **INV-specific motif**, not a universal family mark.

The motif remains visually secondary to the identifier. If a function requires too much detail to fit cleanly, simplify the motif rather than enlarge or overcrowd the icon.

## 4. Color

Each app may use one distinct accent color. Use that accent consistently for:

- frame;
- identifier;
- motif.

The white inner field remains common.

Color should help distinguish apps, but the family must still look related through geometry and hierarchy without relying on color alone.

Final app HEX values are frozen only when the production family is approved.

## 5. Production master

The final `master/CY_ICON_MASTER.svg` should freeze at least:

- canvas and live area;
- outer frame bounds;
- frame thickness;
- corner radius;
- identifier zone;
- motif zone;
- vertical spacing between identifier and motif;
- safe optical-adjustment limits;
- common stroke language.

Once frozen, an app icon is produced by changing only the approved variables:

1. identifier;
2. accent color;
3. lower motif;
4. small optical corrections required by letter/motif shape.

The master must remain editable vector artwork. Generative output is concept material, not the geometry source of truth.

## 6. Small-size Windows production

Formal Windows production should inspect native layers at least at:

`16 / 24 / 32 / 48 / 64 / 128 / 256 px`

Do not mechanically downscale one 256 px image and assume every layer is finished.

Allowed small-size corrections include:

- stroke thickening;
- gap simplification;
- motif simplification;
- identifier size adjustment;
- pixel snapping;
- optical centering.

The perceived identity, family silhouette, and identifier/motif hierarchy must remain stable across sizes.

## 7. Production workflow

Use this order:

1. Use the current CYInvoice/INV and accepted concept board as visual source material.
2. Build and approve one precision square-family vector master.
3. Produce the full family side by side: INV / ACC / ENV / Auto / WTM / CVT / CAL.
4. Adjust identifiers, motifs, and app accent colors while preserving the master skeleton.
5. Review the whole family together, not one icon in isolation.
6. Export production PNG layers and ICO where needed.
7. Verify source/derived asset integrity according to repository binary-asset rules.
8. After the family is approved, each app owner AI copies only that app's approved production assets from AITeam into the target project.

Family-level changes always return to AITeam first.

## 8. Repository scope

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

## 9. AI / designer handoff

Before creating or revising a family icon, use this document and the production master. Do not invent a fresh icon language per project.

Reusable brief:

```text
Create or revise a CY Apps desktop icon from the approved CY square-family master.

Keep unchanged:
- square family body and live area
- frame thickness and corner language
- white interior
- identifier/motif vertical hierarchy
- overall visual mass and clear-space rhythm

App-specific variables:
- Identifier: <IDENTIFIER>
- Accent: <APP ACCENT>
- Lower motif: <ONE SIMPLE APP-SPECIFIC MOTIF>

Requirements:
- flat, clean, high-contrast Windows business-software appearance
- identifier remains dominant
- motif remains secondary
- no heavy shadow, 3D, gloss, or decorative clutter
- do not add INV's two lines to other apps unless the motif genuinely calls for them
- preserve small-size readability
- compare side by side with the rest of the family before acceptance
```

AI-generated images may be used to explore a motif or color idea, but the accepted result must be rebuilt/normalized against the editable family master before production.

## 10. Acceptance checklist

Before an icon is accepted:

- frame/body matches the approved master;
- apparent size is comparable with other members;
- identifier block has comparable visual weight;
- identifier is optically centered;
- motif is simple, app-specific, and secondary;
- one-color + white family system is preserved;
- no accidental extra decoration is present;
- icon remains readable at 48 px and at least one smaller size;
- the family still looks coherent when all icons are shown together;
- production source and derived assets are traceable and reproducible.

## 11. Historical INV geometry — reference only

Earlier measurement of the preserved CYInvoice production icon found an approximately **80–83% canvas width** and **86–88% canvas height** visible live area, with the 48 px layer around **39 × 42 px**.

These measurements remain useful for understanding the historical source, but they are **not** the future square-family geometry target. The production master created in this package supersedes those values once approved.

## 12. Current next step

The next family task is not another document pass. It is to turn the accepted INV-derived square direction into the precision `master/CY_ICON_MASTER.svg`, then produce the complete seven-icon family from that master.
