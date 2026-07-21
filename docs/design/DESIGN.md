---
version: alpha
name: "Deep Teal Corporate"
description: "Harris International Group's site uses a deep teal-to-dark-navy color system anchored by a full-bleed hero with a starry mountain backdrop. The brand color #008099 drives primary CTAs and links, while #002c36 and #000000 provide dark surface depth. Typography layers Montserrat (bold display), Inter Tight (UI and body), and Roboto (supporting text), creating a structured three-family hierarchy. Pill-shaped buttons (50px radius) and a restrained shadow vocabulary signal a confident, corporate-acquisition brand identity."
colors:
  deep-navy: "#002c36"
  ice-blue-surface: "#e0eaf3"
  light-gray-surface: "#f7f8f9"
  light-teal-accent: "#67bfd0"
  brand-teal: "#008099"
  mid-gray-text: "#666666"
  page-black: "#000000"
  pure-white: "#ffffff"
  slate-text: "#425466"
  border-gray: "#cccccc"
typography:
  hero-display:
    fontFamily: "Montserrat"
    fontSize: "50px"
    fontWeight: "700"
    lineHeight: "55px"
  section-heading:
    fontFamily: "Montserrat"
    fontSize: "30px"
    fontWeight: "700"
    lineHeight: "42px"
  card-heading:
    fontFamily: "Inter Tight"
    fontSize: "32px"
    fontWeight: "700"
    lineHeight: "36.8px"
  sub-heading:
    fontFamily: "Inter Tight"
    fontSize: "24px"
    fontWeight: "700"
    lineHeight: "27.6px"
  body-primary:
    fontFamily: "Inter Tight"
    fontSize: "18px"
    fontWeight: "400"
    lineHeight: "27px"
  body-roboto:
    fontFamily: "Roboto"
    fontSize: "18px"
    fontWeight: "400"
    lineHeight: "30.6px"
  ui-label:
    fontFamily: "Inter Tight"
    fontSize: "14px"
    fontWeight: "500"
    lineHeight: "16.1px"
    letterSpacing: "1px"
  nav-item:
    fontFamily: "Inter Tight"
    fontSize: "18px"
    fontWeight: "400"
    lineHeight: "20.7px"
  cta-button-label:
    fontFamily: "Inter Tight"
    fontSize: "14px"
    fontWeight: "700"
    lineHeight: "18.4px"
  small-label-tracked:
    fontFamily: "Inter Tight"
    fontSize: "13px"
    fontWeight: "500"
    lineHeight: "14.95px"
    letterSpacing: "1px"
rounded:
  pill: "50px"
  card: "10px"
  search-input: "20px"
spacing:
  xs: "5px"
  sm: "10px"
  md-sm: "15px"
  md: "16px"
  md-lg: "20px"
  lg: "24px"
  xl: "30px"
  2xl: "36px"
  3xl: "50px"
  4xl: "90px"
  5xl: "100px"
---

## Overview

Harris International Group's site uses a deep teal-to-dark-navy color system anchored by a full-bleed hero with a starry mountain backdrop. The brand color #008099 drives primary CTAs and links, while #002c36 and #000000 provide dark surface depth. Typography layers Montserrat (bold display), Inter Tight (UI and body), and Roboto (supporting text), creating a structured three-family hierarchy. Pill-shaped buttons (50px radius) and a restrained shadow vocabulary signal a confident, corporate-acquisition brand identity.

**Signature traits:**
- Dual typeface system: Pairs Montserrat and Inter Tight across the type hierarchy.
- Soft, rounded geometry: Generous corner rounding up to 50px.
- Layered elevation: Depth comes from 2 validated shadow tokens.

## Colors

The palette uses 10 validated color tokens across 1 theme profile. Semantic roles stay attached to observed usage so generation agents can choose accents without inventing new color meaning.

**Semantic naming:**
- **action-text** maps to `brand-teal`: Role "text" is grounded by usage context "Primary CTA buttons, links, icon accents, and interactive highlights throughout the site".
- **surface-background** maps to `deep-navy`: Role "background" is grounded by usage context "Dark hero section background, footer surface, and deep brand overlay areas".
- **surface-text** maps to `slate-text`: Role "text" is grounded by usage context "Secondary body text, descriptive paragraphs, and supporting UI labels on light backgrounds".
- **content-text** maps to `page-black`: Role "text" is grounded by usage context "Primary text on light sections, nav items, and general body copy".

### Text Scale
- **Brand Teal** (#008099): Primary CTA buttons, links, icon accents, and interactive highlights throughout the site. Role: text. {authored: rgb(0, 128, 153), space: rgb}
- **Mid Gray Text** (#666666): Muted secondary labels and de-emphasized UI text. Role: text. {authored: rgb(102, 102, 102), space: rgb}
- **Page Black** (#000000): Primary text on light sections, nav items, and general body copy. Role: text. {authored: rgb(0, 0, 0), space: rgb}
- **Pure White** (#ffffff): Hero headings, nav text on dark backgrounds, CTA button labels, and light section body text. Role: text. {authored: rgb(255, 255, 255), space: rgb}
- **Slate Text** (#425466): Secondary body text, descriptive paragraphs, and supporting UI labels on light backgrounds. Role: text. {authored: rgb(66, 84, 102), space: rgb}

### Interactive
- **Border Gray** (#cccccc): Search input border and subtle divider lines. Role: border. {authored: rgb(204, 204, 204), space: rgb}

### Surface & Shadows
- **Deep Navy** (#002c36): Dark hero section background, footer surface, and deep brand overlay areas. Role: background. {authored: rgb(0, 44, 54), space: rgb, alpha: 0}
- **Ice Blue Surface** (#e0eaf3): Light section backgrounds, card surfaces, and subtle alternating row fills. Role: background. {authored: rgb(224, 234, 243), space: rgb}
- **Light Gray Surface** (#f7f8f9): Near-white section backgrounds and subtle content area fills. Role: background. {authored: rgb(247, 248, 249), space: rgb}
- **Light Teal Accent** (#67bfd0): Localized accent highlights, icon tints, and decorative teal elements. Role: background. {authored: rgb(103, 191, 208), space: rgb}

## Typography

Typography uses Montserrat, Inter Tight, Roboto across extracted hierarchy roles. Keep hierarchy mapped to these token rows before adding decorative type styles.

Mixes Montserrat and Inter Tight and Roboto for visual contrast. Weight range spans bold, regular, medium. Sizes range from 13px to 50px.

### Font Roles
- **Headline Font**: Montserrat
- **Body Font**: Inter Tight
<!-- Corrected 2026-07-20: extraction stated Montserrat for body, contradicting the type-scale
     evidence below where all body/UI roles are Inter Tight. Body = Inter Tight is authoritative. -->

### Type Scale Evidence
| Role | Font | Size | Weight | Line Height | Letter Spacing | Stack / Features | Notes |
|------|------|------|--------|-------------|----------------|------------------|-------|
| Primary hero headline — large, bold display text over dark hero backgrounds | Montserrat | 50px | 700 | 55px | normal | Montserrat | Extracted token |
| Section-level headings and major content titles | Montserrat | 30px | 700 | 42px | normal | Montserrat | Extracted token |
| Card and sub-section headings | Inter Tight | 32px | 700 | 36.8px | normal | Inter Tight | Extracted token |
| Tertiary headings and feature titles | Inter Tight | 24px | 700 | 27.6px | normal | Inter Tight | Extracted token |
| Main body copy and paragraph text | Inter Tight | 18px | 400 | 27px | normal | Inter Tight | Extracted token |
| Supporting body text and secondary content blocks | Roboto | 18px | 400 | 30.6px | normal | Roboto | Extracted token |
| Uppercase labels, eyebrow text, and small UI tags with tracked spacing | Inter Tight | 14px | 500 | 16.1px | 1px | Inter Tight | Extracted token |
| Primary navigation links | Inter Tight | 18px | 400 | 20.7px | normal | Inter Tight | Extracted token |
| Button text inside pill-shaped CTAs | Inter Tight | 14px | 700 | 18.4px | normal | Inter Tight | Extracted token |
| Micro-labels and category tags with letter-spacing | Inter Tight | 13px | 500 | 14.95px | 1px | Inter Tight | Extracted token |

## Layout

Responsive system uses 3 breakpoint tier(s): mobile, tablet, desktop.

This system uses a 5px base grid with scale values 5, 10, 15, 16, 20, 24, 30, 36, 50, 90, 100.

### Responsive Strategy
- **mobile (510-992px)**: Constrain layout for small viewports and prioritize vertical stacking.
- **tablet (768-1139px)**: Increase spacing and column structure for medium-width viewports.
- **desktop (>= 1100px)**: Expand layout density and horizontal composition for wide viewports.

### Spacing System
| Token | Value | Px | Notes |
|------|-------|----|-------|
| xs | 5px | 5 | Extracted spacing token |
| sm | 10px | 10 | Extracted spacing token |
| md-sm | 15px | 15 | Extracted spacing token |
| md | 16px | 16 | Extracted spacing token |
| md-lg | 20px | 20 | Extracted spacing token |
| lg | 24px | 24 | Extracted spacing token |
| xl | 30px | 30 | Extracted spacing token |
| 2xl | 36px | 36 | Extracted spacing token |
| 3xl | 50px | 50 | Extracted spacing token |
| 4xl | 90px | 90 | Extracted spacing token |
| 5xl | 100px | 100 | Extracted spacing token |

## Elevation & Depth

Keep depth flat unless validated shadow or interaction evidence appears in the extraction payload. Do not invent shadows beyond this evidence boundary.

### Shadow Evidence
| Shadow Token | Layers | Details |
|--------------|--------|---------|
| card-elevation | 1 | 0px 10px 20px 0px rgba(37, 82, 95, 0.08) |
| subtle-glow | 1 | 0px 0px 4px 4px rgba(0, 0, 0, 0.05) |

### Interaction Signals
| Theme | Signal | Evidence |
|-------|--------|----------|
| Light | outline-color | rgb(0, 0, 0) ; rgb(255, 255, 255) ; rgb(0, 128, 153) |
| Light | outline-width | 3px |
| Light | outline-offset | 0px |
| Light | transform | matrix(1, 0, 0, 1, 0, 0) ; matrix(1, 0, 0, -1, 0, 0) ; matrix(-1, 0, 0, 1, 0, 0) |

## Shapes

Shape language maps directly to rounded tokens. Keep component corners consistent with the role mapping below before introducing bespoke geometry.

### Radius Roles
| Token | Value | Px | Role Mapping |
|------|-------|----|--------------|
| card | 10px | 10 | Cards, panels, modals, table containers |
| search-input | 20px | 20 | Text inputs, selects, search fields |
| pill | 50px | 50 | Buttons (primary and secondary CTAs), badges/chips |
<!-- Corrected 2026-07-20: extraction's role column was misaligned with the token evidence
     (pill is the CTA button shape per the source site, not a large-surface corner). -->

### Geometry Evidence
| Radius Token | Shape | Units |
|--------------|-------|-------|
| pill | 50px | px |
| card | 10px | px |
| search-input | 20px | px |

## Components

(none detected)

## Do's and Don'ts

Guardrails protect Dual typeface system, Soft, rounded geometry, Layered elevation without adding unsupported visual claims.

| Do | Don't |
|----|---------|
| Do maintain consistent spacing using the base grid | Don't make unsupported claims about absent visual features |
| Do maintain WCAG AA contrast ratios (4.5:1 for normal text) | Don't mix rounded and sharp corners in the same view |
| Do use the primary color only for the single most important action per screen |  |
| Do verify evidence before writing new design-system guidance |  |

## Responsive Evidence

### Breakpoints
| Name | Width | Key Changes |
|------|-------|-------------|
| Mobile | <= 400px | (max-width: 400px) |
| Mobile | <= 509px | (max-width: 509px) |
| Mobile | <= 568px | (max-width: 568px) |
| Mobile | <= 575px | (max-width: 575px) |
| Mobile | <= 767px | (max-width: 767px) |
| Breakpoint 6 | <= 860px | screen and (max-width: 860px) |
| Breakpoint 7 | <= 922px | (max-width: 922px) |
| Breakpoint 8 | <= 923px | (max-width: 923px) |
| Breakpoint 9 | <= 960px | (max-width: 960px) |
| Breakpoint 10 | <= 992px | (max-width: 992px) |
| Mobile | >= 510px | (min-width: 510px) |
| Mobile | >= 576px | (min-width: 576px) |
| Tablet | >= 768px | (min-width: 768px) |
| Tablet | >= 769px | screen and (min-width: 769px) |
| Tablet | 922-1139px | (min-width: 922px) and (max-width: 1139px) |
| Tablet | >= 922px | (min-width: 922px) |
| Tablet | >= 960px | (min-width: 960px) |
| Tablet | >= 992px | (min-width: 992px) |
| Tablet | >= 1000px | screen and (min-width: 1000px) |
| Desktop | >= 1100px | screen and (min-width: 1100px) |

## Agent Prompt Guide

### Example Component Prompts
- Create button component using validated primary color role and spacing tokens.
- Create card component with mapped radius role and evidence-backed elevation.
- Create form input component using inferred typography hierarchy and border roles.

### Iteration Guide
1. Start with extracted palette and typography roles only.
2. Map spacing and radius directly from token tables before visual polish.
3. Apply component patterns one section at a time and compare against source intent.
4. Keep elevation claims tied to explicit evidence in output.
5. Iterate with smallest diffs and re-check section hierarchy after each change.

---

# Application Addendum — HIG AI Adoption Platform

**Status: authored extension, not extraction evidence.** Everything above this line was extracted from the HIG marketing site and is preserved verbatim (two corrections noted inline). Everything below is authored for the application context — a data-dense internal tool (dashboards, tables, forms, heatmaps) — and carries the same binding force for L1 review. Changes to this addendum are reviewed commits (see CLAUDE.md §8).

## A1. Type roles for the application

The marketing scale is too large for app density. Map roles as follows; do not use hero-display (50px) anywhere in the app.

| App role | Token basis | Size / weight | Usage |
|---|---|---|---|
| page-title | Montserrat 700 | 30px / lh 38px | One per screen |
| section-title | Inter Tight 700 | 24px / lh 30px | Panel and card group headings |
| card-title | Inter Tight 700 | 18px / lh 24px | Card headers, table titles |
| body | Inter Tight 400 | 15px / lh 22px | Default app body (18px is marketing scale; 15px is the app default) |
| table-cell | Inter Tight 400 | 14px / lh 20px | Data tables |
| label | Inter Tight 500 | 13px / +1px tracking, uppercase | Field labels, eyebrows, column headers |
| button | Inter Tight 700 | 14px | All buttons |

**Roboto is not used in the application.** Two families (Montserrat display, Inter Tight everything else) — the third family adds load weight and inconsistency for no benefit in an app context.

## A2. Semantic & status colours (authored — required by the domain)

The extraction contains no status colours; the platform's core visuals are RAG statuses, maturity levels, and completion states. These are additions, chosen to sit with the extracted palette and to pass WCAG AA in the stated usage.

### RAG status
| Token | Hex | Usage |
|---|---|---|
| rag-green | #1F7A4D | On Track — text/icon on light surfaces; tint #E3F2EA for fills with #1F7A4D text |
| rag-amber | #B45309 | At Risk — text/icon on light; tint #FDF0E0 for fills |
| rag-red | #B3362B | Off Track / overdue — text/icon on light; tint #FBE9E7 for fills |

Never colour-only: RAG indicators always pair colour with a label or icon.

### AI-DLC maturity ramp (L0–L3)
Sequential ramp built from the extracted palette; used in heatmaps, league tables, level badges.

| Level | Fill | On-fill text |
|---|---|---|
| L0 | #D5DDE3 (derived neutral from ice-blue family) | page-black |
| L1 | light-teal-accent #67bfd0 | deep-navy #002c36 |
| L2 | brand-teal #008099 | pure-white |
| L3 | deep-navy #002c36 | pure-white |

Heatmap cells always display the level number/value in the cell — colour is reinforcement, not the sole encoding.

### Feedback states
success = rag-green · warning = rag-amber · error = rag-red · info = brand-teal. Error text on white uses #B3362B (AA-compliant); never light-teal.

## A3. Colour usage rules for the app

- **Light UI by default.** App surfaces are pure-white / light-gray-surface (#f7f8f9); ice-blue-surface (#e0eaf3) for alternating rows and subtle panel fills. deep-navy is reserved for the app shell (top nav/sidebar) and data-viz — not for content panels.
- **brand-teal (#008099)** is links, primary buttons, active nav, focus accents. It passes AA on white at ~4.6:1 — acceptable for body-size text and above; for text below 14px use deep-navy or slate-text instead.
- **light-teal-accent (#67bfd0) is never text on light backgrounds** (fails AA). Decorative fills, icon tints, L1 heatmap cells only.
- One primary (teal, filled, pill) button per view; secondary buttons are pill outline (border brand-teal, text brand-teal); destructive buttons use rag-red.

## A4. Components (authored)

- **Buttons:** pill radius; filled primary (brand-teal/white text), outline secondary, text-only tertiary; 40px height default, 32px compact in table rows; disabled = 40% opacity, no colour change.
- **Inputs & selects:** search-input radius (20px is heavy for dense forms — use 8px for standard form fields, 20px reserved for standalone search boxes); 1px border-gray, focus ring per A5; label above field in `label` role; inline validation message in feedback colour below field.
- **Cards/panels:** card radius (10px), white surface, card-elevation shadow; no nested shadows.
- **Tables:** table-cell type role; header row in `label` role on light-gray-surface; row hover #f7f8f9; zebra optional with ice-blue at 40%; right-align numerics; paginate above 25 rows; sticky header on scroll.
- **Badges/chips:** pill radius, `small-label-tracked` role; used for maturity levels, stages, RAG.
- **Charts (rollups, trends, distributions):** categorical series drawn from brand-teal → light-teal → deep-navy → slate-text before any new hue; maturity data always uses the A2 ramp; gridlines border-gray at 50%; no 3D, no gradients.
- **Empty/loading:** skeletons in light-gray-surface; empty states use slate-text with one clear action.

## A5. Interaction & accessibility (binding)

- **Focus:** 3px outline in brand-teal, 2px offset, on every interactive element (evidence: extraction interaction signals show 3px outlines). Never remove focus outlines.
- **Contrast:** WCAG 2.2 AA minimum everywhere (4.5:1 normal text, 3:1 large text and UI components) — this is acceptance criteria per constitution Art. IX.5.
- **Hit targets:** minimum 40×40px (24px in dense table rows with adequate spacing).
- **Motion:** transitions ≤200ms, opacity/transform only; respect `prefers-reduced-motion`.
- **Colour independence:** every status, level, and trend encoded by colour also carries text or iconography.

## A6. Layout & density

- App frame: fixed top bar (deep-navy) + left nav; content max-width 1440px, min supported 1280px — this is a desktop-first internal tool; tablet gets a functional single-column fallback; phones are out of scope for v1 (assessment happens at desks).
- Spacing in app views uses the sm/md/lg/xl steps (10/16/24/30). 4xl/5xl (90/100px) are marketing-scale — do not use in app screens.
- Forms: single column, 16px between fields, 30px between groups; assessment flow one dimension per screen-section with the level descriptors inline.

## A7. Don'ts (app-specific, additive to the extraction guardrails)

- Don't use hero-display type, full-bleed heroes, or the starry backdrop in the app.
- Don't use Roboto.
- Don't put light-teal text on light surfaces.
- Don't encode any status by colour alone.
- Don't introduce a colour, radius, shadow, or type size not in this file — additions go through a reviewed commit to this addendum.
