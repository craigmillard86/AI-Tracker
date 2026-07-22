/** Maturity level for a dimension mean = nearest level 0–3 (presentation only; drives the A2 ramp
 * fill and the level badge). Kept in its own module so the component file exports only a component
 * (react-refresh/only-export-components). */
export function levelOfMean(mean: number): number {
  return Math.min(3, Math.max(0, Math.round(mean)));
}
