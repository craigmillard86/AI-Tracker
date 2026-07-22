import type { ReactNode } from 'react';

export type TrendDirection = 'up' | 'down' | 'flat';

export interface StatTileProps {
  /** Eyebrow label (label type role). */
  label: string;
  /** The headline value — already formatted (a level badge, a number, an em dash). */
  value: ReactNode;
  /** Supporting sub-text below the value. */
  sub?: ReactNode;
  /** Trend indicator — an ARROW GLYPH + TEXT, never colour alone (DESIGN.md A5). */
  trend?: { direction: TrendDirection; text: string };
  /** Optional slot to the right of the value (e.g. a TrendSparkline). */
  aside?: ReactNode;
  /** Alert treatment (e.g. overdue) — still paired with text, colour is not the sole signal. */
  alert?: boolean;
}

const ARROW: Record<TrendDirection, string> = { up: '▲', down: '▼', flat: '■' };

/**
 * Headline number + label + optional trend (DESIGN.md A8, dashboard-bu). The trend direction is carried
 * by an arrow GLYPH and the accompanying text, so it never depends on colour (A5 colour-independence).
 */
export function StatTile({ label, value, sub, trend, aside, alert = false }: StatTileProps): JSX.Element {
  return (
    <div className={alert ? 'stat-tile stat-tile-alert' : 'stat-tile'}>
      <div className="stat-tile-label">{label}</div>
      <div className="stat-tile-body">
        <div className="stat-tile-value">{value}</div>
        {aside && <div className="stat-tile-aside">{aside}</div>}
      </div>
      {trend && (
        <div className={`stat-tile-trend stat-tile-trend-${trend.direction}`}>
          <span aria-hidden="true">{ARROW[trend.direction]}</span> {trend.text}
        </div>
      )}
      {sub && <div className="stat-tile-sub">{sub}</div>}
    </div>
  );
}
