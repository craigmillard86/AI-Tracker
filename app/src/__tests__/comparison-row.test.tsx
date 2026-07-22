import { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { ComparisonRow, type ComparisonRowLevel } from '../components/ComparisonRow/ComparisonRow';
import { strings } from '../strings';

// Deliberately fictional dimension/level wording — Art. II.4 / the framework grep-guard forbids
// real framework content anywhere in app/src, tests included.
const LEVELS: ComparisonRowLevel[] = [0, 1, 2, 3].map((level) => ({
  level,
  levelName: `Fictional option ${level}`,
  descriptorText: `Fictional descriptor ${level} for testing only.`,
}));

function Harness({
  selfScore,
  initialManagerScore,
  initialComment = '',
  disabled = false,
  // Represents the server-provided `MemberAssessmentResponse.commentThreshold` a caller would pass
  // through in real use — the test fixture's own literal, not a client-side production constant.
  commentThreshold = 2,
  defaultManagerScore,
  defaultCommentRequired,
}: {
  selfScore: number;
  initialManagerScore: number;
  initialComment?: string;
  disabled?: boolean;
  commentThreshold?: number;
  defaultManagerScore?: number;
  defaultCommentRequired?: boolean;
}): JSX.Element {
  const [managerScore, setManagerScore] = useState(initialManagerScore);
  const [comment, setComment] = useState(initialComment);
  return (
    <table>
      <tbody>
        <ComparisonRow
          dimensionId="dim-1"
          dimensionName="Fictional dimension"
          levels={LEVELS}
          selfScore={selfScore}
          managerScore={managerScore}
          comment={comment}
          commentThreshold={commentThreshold}
          defaultManagerScore={defaultManagerScore}
          defaultCommentRequired={defaultCommentRequired}
          disabled={disabled}
          onManagerScoreChange={(_, level) => setManagerScore(level)}
          onCommentChange={(_, value) => setComment(value)}
        />
      </tbody>
    </table>
  );
}

describe('ComparisonRow (HAP-9; FR-008/009/010/011; DESIGN.md A8)', () => {
  it('prints both the self score and the manager score as text, and shows "agree" when they match', () => {
    render(<Harness selfScore={2} initialManagerScore={2} />);

    // Both the self-score badge and the manager select's own "L2" option render "L2" text — scope
    // the assertion to the read-only self badge specifically.
    expect(screen.getByText(strings.assessment.levelAbbrev(2), { selector: '.level-badge' })).toBeTruthy();
    expect(screen.getByText(strings.moderation.agreeChip)).toBeTruthy();
  });

  it('updates the manager score via the labelled select and recomputes divergence', () => {
    render(<Harness selfScore={1} initialManagerScore={1} />);

    const select = screen.getByLabelText(strings.moderation.yourScoreLabel('Fictional dimension'));
    fireEvent.change(select, { target: { value: '3' } });

    expect(screen.getByText(strings.moderation.divergenceValue(2))).toBeTruthy();
  });

  it('shows a forced-comment error state at divergence >= 2 with no comment, and blocks it once a comment is entered', () => {
    render(<Harness selfScore={3} initialManagerScore={1} />);

    expect(screen.getByText(strings.moderation.commentRequiredError)).toBeTruthy();
    const textarea = screen.getByLabelText(strings.moderation.commentLabel('Fictional dimension'));
    expect(textarea).toHaveAttribute('aria-invalid', 'true');

    fireEvent.change(textarea, { target: { value: 'Evidence reviewed — moderating down.' } });

    expect(screen.queryByText(strings.moderation.commentRequiredError)).toBeFalsy();
  });

  it('does not force a comment when divergence is 1 or 0', () => {
    render(<Harness selfScore={2} initialManagerScore={1} />);

    expect(screen.queryByText(strings.moderation.commentRequiredError)).toBeFalsy();
  });

  describe('commentThreshold is server-driven, never a client literal (code-reviewer follow-up)', () => {
    it('forces a comment at divergence 1 when the server threshold is 1 (not the usual 2)', () => {
      render(<Harness selfScore={2} initialManagerScore={1} commentThreshold={1} />);

      expect(screen.getByText(strings.moderation.commentRequiredError)).toBeTruthy();
    });

    it('does not force a comment at divergence 2 when the server threshold is 3', () => {
      render(<Harness selfScore={3} initialManagerScore={1} commentThreshold={3} />);

      expect(screen.queryByText(strings.moderation.commentRequiredError)).toBeFalsy();
    });
  });

  describe('carried-forward default flagged defaultCommentRequired (FR-063 sustained Δ>=2; panel follow-up)', () => {
    it('shows the required/error state for an untouched default even though its own arithmetic divergence would not obviously trigger it, trusting the server signal', () => {
      // selfScore/managerScore here are equal (live divergence 0) — only the server's own
      // defaultCommentRequired flag on the untouched default drives the requirement, proving the
      // row honours that signal defensively rather than only ever recomputing |self - manager|.
      render(
        <Harness selfScore={2} initialManagerScore={2} defaultManagerScore={2} defaultCommentRequired />,
      );

      expect(screen.getByText(strings.moderation.commentRequiredError)).toBeTruthy();
      const textarea = screen.getByLabelText(strings.moderation.commentLabel('Fictional dimension'));
      expect(textarea).toHaveAttribute('aria-invalid', 'true');
    });

    it('clears once a comment is entered for the untouched flagged default', () => {
      render(
        <Harness selfScore={3} initialManagerScore={1} defaultManagerScore={1} defaultCommentRequired />,
      );

      expect(screen.getByText(strings.moderation.commentRequiredError)).toBeTruthy();

      fireEvent.change(screen.getByLabelText(strings.moderation.commentLabel('Fictional dimension')), {
        target: { value: 'Evidence reviewed — accepting the sustained moderated default.' },
      });

      expect(screen.queryByText(strings.moderation.commentRequiredError)).toBeFalsy();
    });

    it('stops requiring a comment once the manager edits the score away from the flagged default, even with defaultCommentRequired still true', () => {
      render(
        <Harness selfScore={3} initialManagerScore={1} defaultManagerScore={1} defaultCommentRequired />,
      );

      expect(screen.getByText(strings.moderation.commentRequiredError)).toBeTruthy();

      // Manager picks a score that both moves off the default AND no longer diverges >= 2 from self.
      fireEvent.change(screen.getByLabelText(strings.moderation.yourScoreLabel('Fictional dimension')), {
        target: { value: '2' },
      });

      expect(screen.queryByText(strings.moderation.commentRequiredError)).toBeFalsy();
    });
  });

  it('disables the score select and comment field when disabled (read-only review)', () => {
    render(<Harness selfScore={1} initialManagerScore={1} disabled />);

    expect(screen.getByLabelText(strings.moderation.yourScoreLabel('Fictional dimension'))).toBeDisabled();
    expect(screen.getByLabelText(strings.moderation.commentLabel('Fictional dimension'))).toBeDisabled();
  });

  it('calls onManagerScoreChange and onCommentChange with the dimension id', () => {
    const onManagerScoreChange = vi.fn();
    const onCommentChange = vi.fn();
    render(
      <table>
        <tbody>
          <ComparisonRow
            dimensionId="dim-42"
            dimensionName="Fictional dimension"
            levels={LEVELS}
            selfScore={1}
            managerScore={1}
            comment=""
            commentThreshold={2}
            onManagerScoreChange={onManagerScoreChange}
            onCommentChange={onCommentChange}
          />
        </tbody>
      </table>,
    );

    fireEvent.change(screen.getByLabelText(strings.moderation.yourScoreLabel('Fictional dimension')), {
      target: { value: '2' },
    });
    fireEvent.change(screen.getByLabelText(strings.moderation.commentLabel('Fictional dimension')), {
      target: { value: 'note' },
    });

    expect(onManagerScoreChange).toHaveBeenCalledWith('dim-42', 2);
    expect(onCommentChange).toHaveBeenCalledWith('dim-42', 'note');
  });

  it('has no detectable accessibility violations', async () => {
    const { container } = render(<Harness selfScore={3} initialManagerScore={0} />);

    const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(results.violations).toEqual([]);
  });
});
