import { strings } from '../../strings';

export interface SuppressedCellProps {
  /** The server's suppression reason marker — "N<4" or "Complement". Never a count. */
  reason: string;
  /** Inline (a table cell / stat slot) or block (a full-card notice). */
  variant?: 'inline' | 'block';
}

/**
 * The suppressed-aggregate rendering (DESIGN.md A8; FR-071). NEVER a number, NEVER blank: it prints a
 * mid-gray em dash plus the reason text so a screen reader announces "Suppressed, n < 4" rather than a
 * silent gap. There is deliberately no prop for a count — a SuppressedCell has no figure to show, which
 * mirrors the server's F2 guarantee that a suppressed node carries no figure at all.
 */
export function SuppressedCell({ reason, variant = 'inline' }: SuppressedCellProps): JSX.Element {
  return (
    <span className={variant === 'block' ? 'suppressed-cell suppressed-cell-block' : 'suppressed-cell'}>
      <span className="suppressed-cell-dash" aria-hidden="true">
        {strings.dashboard.stubDash}
      </span>
      <span className="suppressed-cell-text">
        {strings.dashboard.suppressedCellText} ({strings.dashboard.suppressedCellReason(reason)})
      </span>
    </span>
  );
}
