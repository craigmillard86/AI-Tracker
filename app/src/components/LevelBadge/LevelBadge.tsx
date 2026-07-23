import { strings } from '../../strings';

export interface LevelBadgeProps {
  /** AI-DLC level (0–3; the register uses 1–3). */
  level: number;
}

/**
 * AI-DLC maturity-level badge (DESIGN.md A2 maturity ramp; mockup `.lvl.lvl-0..3`). The level number is
 * ALWAYS printed as text ("L2") — colour (the L0–L3 ramp tokens) is reinforcement only, never the sole
 * signal (A2 colour-independence). Levels outside 0–3 clamp for the colour class but still print the
 * true number.
 */
export function LevelBadge({ level }: LevelBadgeProps): JSX.Element {
  const ramp = Math.max(0, Math.min(3, Math.round(level)));
  return (
    <span className={`level-badge level-badge-${ramp}`}>{strings.assessment.levelAbbrev(level)}</span>
  );
}
