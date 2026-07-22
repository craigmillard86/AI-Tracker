import { strings } from '../../strings';
import { levelOfMean } from './level';

export interface DimensionBarProps {
  /** Dimension name (framework data). */
  name: string;
  /** Mean moderated score 0–3 (FR-015). */
  mean: number;
}

const MAX = 3;

/**
 * Per-dimension mean bar, hand-rolled SVG (DESIGN.md A8, dashboard-bu). The fill uses the A2 maturity
 * ramp for the mean's nearest level; the numeric value is PRINTED at the bar end (never colour alone).
 * The bar carries `role="img"` with a computed aria-label so a screen reader hears the dimension and its
 * mean; the visible value is marked aria-hidden to avoid a duplicate announcement.
 */
export function DimensionBar({ name, mean }: DimensionBarProps): JSX.Element {
  const clamped = Math.min(MAX, Math.max(0, mean));
  const pct = (clamped / MAX) * 100;
  const level = levelOfMean(mean);
  const meanText = mean.toFixed(1);

  return (
    <span className="dimension-bar">
      <svg
        className="dimension-bar-track"
        viewBox="0 0 100 10"
        preserveAspectRatio="none"
        role="img"
        aria-label={strings.dashboard.dimensionBarLabel(name, meanText)}
      >
        <rect x="0" y="0" width="100" height="10" rx="5" className="dimension-bar-bg" />
        <rect x="0" y="0" width={pct} height="10" rx="5" className={`dimension-bar-fill dimension-bar-fill-${level}`} />
      </svg>
      <span className="dimension-bar-value" aria-hidden="true">
        {meanText}
      </span>
    </span>
  );
}
