/**
 * Documented design-token values, transcribed from docs/design/DESIGN.md.
 * The token-guard test (src/__tests__/tokens.test.ts) asserts that
 * src/design/tokens.css defines each custom property below with the documented
 * value (hex compared case-insensitively). This file is the "documented values"
 * side of that guard — update it and tokens.css together, only via a reviewed
 * change to the design addendum.
 */
export const expectedTokens: Record<string, string> = {
  // Base palette
  '--color-deep-navy': '#002c36',
  '--color-ice-blue-surface': '#e0eaf3',
  '--color-light-gray-surface': '#f7f8f9',
  '--color-light-teal-accent': '#67bfd0',
  '--color-brand-teal': '#008099',
  '--color-mid-gray-text': '#666666',
  '--color-page-black': '#000000',
  '--color-pure-white': '#ffffff',
  '--color-slate-text': '#425466',
  '--color-border-gray': '#cccccc',

  // A2 — RAG status
  '--color-rag-green': '#1F7A4D',
  '--color-rag-green-tint': '#E3F2EA',
  '--color-rag-amber': '#B45309',
  '--color-rag-amber-tint': '#FDF0E0',
  '--color-rag-red': '#B3362B',
  '--color-rag-red-tint': '#FBE9E7',

  // A2 — AI-DLC maturity ramp (L0-L3)
  '--color-level-0': '#D5DDE3',
  '--color-level-1': '#67bfd0',
  '--color-level-2': '#008099',
  '--color-level-3': '#002c36',

  // A2 — feedback states
  '--color-feedback-success': '#1F7A4D',
  '--color-feedback-warning': '#B45309',
  '--color-feedback-error': '#B3362B',
  '--color-feedback-info': '#008099',

  // A1 — type families
  '--font-family-display': '"Montserrat", sans-serif',
  '--font-family-ui': '"Inter Tight", sans-serif',

  // A1 — application type roles
  '--type-page-title-size': '30px',
  '--type-page-title-line': '38px',
  '--type-page-title-weight': '700',
  '--type-section-title-size': '24px',
  '--type-section-title-line': '30px',
  '--type-section-title-weight': '700',
  '--type-card-title-size': '18px',
  '--type-card-title-line': '24px',
  '--type-card-title-weight': '700',
  '--type-body-size': '15px',
  '--type-body-line': '22px',
  '--type-body-weight': '400',
  '--type-table-cell-size': '14px',
  '--type-table-cell-line': '20px',
  '--type-table-cell-weight': '400',
  '--type-label-size': '13px',
  '--type-label-weight': '500',
  '--type-label-tracking': '1px',
  '--type-button-size': '14px',
  '--type-button-weight': '700',

  // A6 — spacing scale
  '--space-xs': '5px',
  '--space-sm': '10px',
  '--space-md-sm': '15px',
  '--space-md': '16px',
  '--space-md-lg': '20px',
  '--space-lg': '24px',
  '--space-xl': '30px',
  '--space-2xl': '36px',
  '--space-3xl': '50px',
  '--space-4xl': '90px', // marketing-scale — NOT for app screens (DESIGN.md A6/A7)
  '--space-5xl': '100px', // marketing-scale — NOT for app screens (DESIGN.md A6/A7)

  // Radius roles
  '--radius-pill': '50px',
  '--radius-card': '10px',
  '--radius-search': '20px',
  '--radius-input': '8px',

  // Shadow Evidence — card-elevation, the one validated card shadow (DESIGN.md "Elevation & Depth")
  '--shadow-card': '0px 10px 20px 0px rgba(37, 82, 95, 0.08)',
};
