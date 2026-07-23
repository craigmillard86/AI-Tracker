import { strings } from '../../strings';

export interface StaleRowFlagProps {
  /** Whole days since the initiative's last update (computed by the caller from `lastUpdateAt`). */
  days: number;
}

/**
 * Stale-row indicator (DESIGN.md A8): renders nothing at 7 days or fresher; an amber chip when more
 * than 7 and up to 14 days stale; a red chip beyond 14 days. The day count is ALWAYS carried in the
 * chip text (e.g. "11d stale") — colour is reinforcement only, never the sole signal (A5/A7). The row
 * keeps normal contrast; only this chip changes.
 */
export function StaleRowFlag({ days }: StaleRowFlagProps): JSX.Element | null {
  if (days <= 7) {
    return null;
  }
  const variant = days > 14 ? 'red' : 'amber';
  return <span className={`stale-flag stale-flag-${variant}`}>{strings.register.staleFlag(days)}</span>;
}
