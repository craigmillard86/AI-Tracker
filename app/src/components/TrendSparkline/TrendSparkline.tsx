export interface TrendSparklineProps {
  /** The series values (oldest → newest). Fewer than 2 points renders nothing. */
  values: number[];
  /** Domain bounds for scaling (defaults to the maturity range 0–3). */
  min?: number;
  max?: number;
}

/**
 * Compact cycle-trend line, hand-rolled SVG (DESIGN.md A8). Deliberately DECORATIVE — `aria-hidden`, no
 * fill, brand-teal stroke — because the values it depicts are always carried by adjacent text (the
 * StatTile value + its trend text). A screen reader gets the number and direction there, not from here.
 */
export function TrendSparkline({ values, min = 0, max = 3 }: TrendSparklineProps): JSX.Element | null {
  if (values.length < 2) {
    return null;
  }

  const width = 118;
  const height = 32;
  const pad = 3;
  const span = Math.max(1e-9, max - min);
  const stepX = (width - pad * 2) / (values.length - 1);

  const coords = values.map((v, i) => {
    const clamped = Math.min(max, Math.max(min, v));
    const x = pad + i * stepX;
    const y = pad + (1 - (clamped - min) / span) * (height - pad * 2);
    return { x, y };
  });

  const points = coords.map((c) => `${c.x.toFixed(1)},${c.y.toFixed(1)}`).join(' ');
  const last = coords[coords.length - 1] ?? { x: pad, y: pad };

  return (
    <svg className="trend-sparkline" width={width} height={height} viewBox={`0 0 ${width} ${height}`} aria-hidden="true" focusable="false">
      <polyline className="trend-sparkline-line" fill="none" points={points} strokeLinejoin="round" strokeLinecap="round" />
      <circle className="trend-sparkline-dot" cx={last.x.toFixed(1)} cy={last.y.toFixed(1)} r="2.6" />
    </svg>
  );
}
