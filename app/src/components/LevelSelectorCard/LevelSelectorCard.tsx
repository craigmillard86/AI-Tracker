import type { KeyboardEvent } from 'react';
import { strings } from '../../strings';

export interface LevelSelectorCardProps {
  /** Shared `name` for the four cards belonging to one dimension's radio group. */
  groupName: string;
  /** 0–3 maturity level this card represents. */
  level: number;
  /** Level name from the framework data (never hard-coded — always passed in from the API). */
  levelName: string;
  /** Descriptor text for this dimension at this level (framework data, never hard-coded). */
  descriptorText: string;
  /** Effective current selection for the dimension (selfScore ?? priorScore) === level. */
  checked: boolean;
  /** True when `level` equals the prior-cycle score — shown independently of `checked` (a card
   * can carry the "last month" pill without being the currently selected one). */
  isPrior: boolean;
  onSelect: (level: number) => void;
  disabled?: boolean;
}

/**
 * One selectable level-descriptor card (DESIGN.md A8). The four cards for a dimension form a
 * native radio group via a shared `name` — the visually-hidden `<input type="radio">` inside the
 * `<label>` gives roving tabindex and Space/Enter selection from the browser for free. Arrow-key
 * movement between the group's cards is implemented explicitly here (rather than relied on as
 * native browser behaviour) so it is deterministic across every environment, including headless
 * test runners where the browser's built-in radio-group key handling isn't present. Selected
 * state is carried by a 2px brand-teal border AND a check icon — never colour alone (DESIGN.md
 * A5/A7).
 */
export function LevelSelectorCard({
  groupName,
  level,
  levelName,
  descriptorText,
  checked,
  isPrior,
  onSelect,
  disabled = false,
}: LevelSelectorCardProps): JSX.Element {
  const descId = `${groupName}-level-${level}-desc`;

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>): void {
    const forward = event.key === 'ArrowRight' || event.key === 'ArrowDown';
    const backward = event.key === 'ArrowLeft' || event.key === 'ArrowUp';
    if (!forward && !backward) {
      return;
    }
    event.preventDefault();
    const group = Array.from(
      event.currentTarget
        .closest('fieldset, [role="radiogroup"]')
        ?.querySelectorAll<HTMLInputElement>(`input[type="radio"][name="${groupName}"]`) ?? [],
    );
    if (group.length === 0) {
      return;
    }
    const currentIndex = group.indexOf(event.currentTarget);
    const delta = forward ? 1 : -1;
    const next = group[(currentIndex + delta + group.length) % group.length];
    next.focus();
    next.click();
  }

  const classNames = ['level-card'];
  if (checked) {
    classNames.push('level-card-selected');
  }
  if (isPrior) {
    classNames.push('level-card-prior');
  }
  if (disabled) {
    classNames.push('level-card-disabled');
  }

  return (
    <label className={classNames.join(' ')}>
      {isPrior && <span className="level-card-prior-pill">{strings.assessment.lastMonthPill}</span>}
      <input
        className="level-card-input"
        type="radio"
        name={groupName}
        value={level}
        checked={checked}
        disabled={disabled}
        onChange={() => onSelect(level)}
        // A native radio's `change` event only fires on a checked-state transition — clicking a
        // card that is already checked (e.g. re-confirming a pre-populated prior-cycle level,
        // exactly the FR-062 "no-change month" flow) would otherwise be silently ignored. `click`
        // fires on every activation regardless, so it is the one that actually records the
        // person's confirmation for this cycle; `onSelect` is idempotent so the harmless double
        // call on an actual value change (click then change) is safe.
        onClick={() => onSelect(level)}
        onKeyDown={handleKeyDown}
        aria-label={strings.assessment.levelOptionLabel(level, levelName)}
        aria-describedby={descId}
      />
      <span className="level-card-head">
        <span className={`level-badge level-badge-${level}`}>{strings.assessment.levelAbbrev(level)}</span>
        {checked && (
          <svg className="level-card-check" viewBox="0 0 16 16" aria-hidden="true" focusable="false">
            <path
              d="M3 8.5 6.5 12 13 4.5"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
        )}
        <span className="level-card-name">{levelName}</span>
      </span>
      <span className="level-card-desc" id={descId}>
        {descriptorText}
      </span>
    </label>
  );
}
