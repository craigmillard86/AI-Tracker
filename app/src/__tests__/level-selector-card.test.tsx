import { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LevelSelectorCard } from '../components/LevelSelectorCard/LevelSelectorCard';

// Deliberately fictional level/dimension wording (not the real framework content) — Art. II.4 /
// the framework grep-guard forbids real framework strings from appearing anywhere in app/src,
// including tests.
const OPTIONS = [
  { level: 0, levelName: 'Starter option', descriptorText: 'A first fictional descriptor for testing.' },
  { level: 1, levelName: 'Second option', descriptorText: 'A second fictional descriptor for testing.' },
  { level: 2, levelName: 'Third option', descriptorText: 'A third fictional descriptor for testing.' },
  { level: 3, levelName: 'Fourth option', descriptorText: 'A fourth fictional descriptor for testing.' },
];

function LevelSetHarness({ initial = 1 }: { initial?: number }): JSX.Element {
  const [selected, setSelected] = useState(initial);
  return (
    <fieldset>
      <legend>Pick a level</legend>
      {OPTIONS.map((option) => (
        <LevelSelectorCard
          key={option.level}
          groupName="test-dimension"
          level={option.level}
          levelName={option.levelName}
          descriptorText={option.descriptorText}
          checked={selected === option.level}
          isPrior={option.level === 1}
          onSelect={setSelected}
        />
      ))}
    </fieldset>
  );
}

describe('LevelSelectorCard (DESIGN.md A8)', () => {
  it('forms a native radio group of four options with exactly one selected', () => {
    render(<LevelSetHarness initial={1} />);

    const radios = screen.getAllByRole('radio');
    expect(radios).toHaveLength(4);
    expect(radios.filter((radio) => (radio as HTMLInputElement).checked)).toHaveLength(1);
    expect((radios[1] as HTMLInputElement).checked).toBe(true);
  });

  it('updates the selection when a different card is clicked', () => {
    render(<LevelSetHarness initial={1} />);

    fireEvent.click(screen.getByRole('radio', { name: /Third option/ }));

    expect((screen.getByRole('radio', { name: /Third option/ }) as HTMLInputElement).checked).toBe(true);
    expect((screen.getByRole('radio', { name: /Second option/ }) as HTMLInputElement).checked).toBe(false);
  });

  it('moves the selection with ArrowRight/ArrowLeft keyboard navigation', () => {
    render(<LevelSetHarness initial={0} />);

    const first = screen.getByRole('radio', { name: /Starter option/ }) as HTMLInputElement;
    first.focus();
    fireEvent.keyDown(first, { key: 'ArrowRight' });

    const second = screen.getByRole('radio', { name: /Second option/ }) as HTMLInputElement;
    expect(second.checked).toBe(true);
    expect(document.activeElement).toBe(second);

    fireEvent.keyDown(second, { key: 'ArrowLeft' });
    expect(first.checked).toBe(true);
    expect(document.activeElement).toBe(first);
  });

  it('wraps ArrowRight navigation from the last card back to the first', () => {
    render(<LevelSetHarness initial={3} />);

    const last = screen.getByRole('radio', { name: /Fourth option/ }) as HTMLInputElement;
    last.focus();
    fireEvent.keyDown(last, { key: 'ArrowRight' });

    const first = screen.getByRole('radio', { name: /Starter option/ }) as HTMLInputElement;
    expect(first.checked).toBe(true);
  });

  it('carries selected state with more than colour: a visible check indicator is present', () => {
    render(<LevelSetHarness initial={2} />);

    const selectedCard = screen.getByRole('radio', { name: /Third option/ }).closest('label');
    expect(selectedCard?.querySelector('.level-card-check')).toBeTruthy();

    const unselectedCard = screen.getByRole('radio', { name: /Fourth option/ }).closest('label');
    expect(unselectedCard?.querySelector('.level-card-check')).toBeFalsy();
  });

  it('re-selecting an already-checked card still calls onSelect (FR-062 no-change re-confirmation — a native radio fires no change event on a click that does not alter its checked state)', () => {
    const onSelect = vi.fn();
    render(
      <LevelSelectorCard
        groupName="test-dimension-confirm"
        level={1}
        levelName="Second option"
        descriptorText="A second fictional descriptor for testing."
        checked
        isPrior
        onSelect={onSelect}
      />,
    );

    fireEvent.click(screen.getByRole('radio', { name: /Second option/ }));

    expect(onSelect).toHaveBeenCalledWith(1);
  });

  it('shows the "last month" pill on the prior-cycle card independent of the current selection', () => {
    render(<LevelSetHarness initial={2} />);

    const priorCard = screen.getByRole('radio', { name: /Second option/ }).closest('label');
    expect(priorCard?.textContent).toContain('Last month');
    expect((screen.getByRole('radio', { name: /Second option/ }) as HTMLInputElement).checked).toBe(false);
  });
});
