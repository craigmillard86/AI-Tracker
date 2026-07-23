import type { KeyboardEvent } from 'react';
import { strings } from '../../strings';

export interface DeclarationLevelPickerProps {
  /** Shared `name` for the four cards' native radio group. */
  groupName: string;
  /** Currently declared level (0-3), or null when nothing is selected yet. */
  value: number | null;
  onSelect: (level: number) => void;
  disabled?: boolean;
}

const LEVELS = [0, 1, 2, 3];

/**
 * Bare 0-3 level picker for the BU weekly declaration (HAP-15; FR-047). A deliberately LIGHTER variant
 * of `LevelSelectorCard` (DESIGN.md A8): the declaration is a whole-BU AI-DLC level, not tied to any one
 * scored dimension, so there is no per-dimension `levelName`/`descriptorText` framework data to show
 * here (unlike the self-assessment's dimension-scoped cards) — the mockup itself shows only the level
 * badge + a short label for this control, not the full descriptor text. Only the level badge is
 * rendered; every visible label routes through the strings module (FR-067). Reuses the `.level-badge`
 * visual (LevelSelectorCard.css, already loaded globally) rather than introducing new colour tokens.
 * Same native-radio-group + explicit arrow-key roving-focus pattern as `LevelSelectorCard`, kept
 * deterministic across headless test runners.
 */
export function DeclarationLevelPicker({
  groupName,
  value,
  onSelect,
  disabled = false,
}: DeclarationLevelPickerProps): JSX.Element {
  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>): void {
    const forward = event.key === 'ArrowRight' || event.key === 'ArrowDown';
    const backward = event.key === 'ArrowLeft' || event.key === 'ArrowUp';
    if (!forward && !backward) {
      return;
    }
    event.preventDefault();
    const group = Array.from(
      event.currentTarget
        .closest('[role="radiogroup"]')
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

  return (
    <div className="decl-level-picker" role="radiogroup" aria-label={strings.buForms.declaredLevelFieldLabel}>
      {LEVELS.map((level) => {
        const checked = value === level;
        return (
          <label key={level} className={`decl-level-card${checked ? ' decl-level-card-selected' : ''}`}>
            <input
              className="decl-level-card-input"
              type="radio"
              name={groupName}
              value={level}
              checked={checked}
              disabled={disabled}
              onChange={() => onSelect(level)}
              // See LevelSelectorCard's identical comment: `click` fires even on re-confirming an
              // already-checked card, which `change` alone would silently drop.
              onClick={() => onSelect(level)}
              onKeyDown={handleKeyDown}
              aria-label={strings.buForms.declaredLevelOptionLabel(level)}
            />
            <span className={`level-badge level-badge-${level}`}>{strings.assessment.levelAbbrev(level)}</span>
          </label>
        );
      })}
    </div>
  );
}
