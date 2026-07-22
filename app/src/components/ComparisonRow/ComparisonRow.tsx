import { isCommentRequired } from './commentRequired';
import { DivergenceFlag } from '../DivergenceFlag/DivergenceFlag';
import { strings } from '../../strings';

export interface ComparisonRowLevel {
  level: number;
  levelName: string;
  descriptorText: string;
}

export interface ComparisonRowProps {
  dimensionId: string;
  dimensionName: string;
  levels: ComparisonRowLevel[];
  /** The individual's self score for this dimension (always printed as text, never colour alone). */
  selfScore: number;
  /** The manager's current selection for this dimension. */
  managerScore: number;
  comment: string;
  /** FR-009's forced-comment divergence threshold — sourced from the member payload's
   * `MemberAssessmentResponse.commentThreshold` (server-owned domain constant). Deliberately no
   * client-side default: the caller must always supply the server's value, never a client literal. */
  commentThreshold: number;
  /** The carry-forward/adopt default this dimension was pre-filled with (FR-063). Together with
   * `defaultCommentRequired`, lets the required/error state hold even before the manager has
   * touched the score — see `isCommentRequired`. */
  defaultManagerScore?: number;
  /** Server-computed: true when `defaultManagerScore` itself diverges at or beyond
   * `commentThreshold` from `selfScore` (a sustained prior forced-comment moderation, FR-063/FR-009). */
  defaultCommentRequired?: boolean;
  disabled?: boolean;
  onManagerScoreChange: (dimensionId: string, level: number) => void;
  onCommentChange: (dimensionId: string, comment: string) => void;
}

/**
 * One dimension row in the manager-moderation table (HAP-9; FR-008/009/010/011; DESIGN.md A8;
 * layout/IA per `docs/design/mockups/assessment-moderation.html`, binding). Both the self and
 * manager scores are always printed as level badges/values — never colour alone (DESIGN.md A5/A7).
 * The comment field becomes required, with an inline error, once the manager's score diverges from
 * the self score by at least `commentThreshold` levels (FR-009's forced-comment invariant, threshold
 * server-owned — see `isCommentRequired`).
 */
export function ComparisonRow({
  dimensionId,
  dimensionName,
  levels,
  selfScore,
  managerScore,
  comment,
  commentThreshold,
  defaultManagerScore,
  defaultCommentRequired,
  disabled = false,
  onManagerScoreChange,
  onCommentChange,
}: ComparisonRowProps): JSX.Element {
  const commentRequired = isCommentRequired(
    selfScore,
    managerScore,
    commentThreshold,
    defaultManagerScore,
    defaultCommentRequired,
  );
  const commentError = commentRequired && comment.trim().length === 0;
  const moderatedDescriptor = levels.find((level) => level.level === managerScore)?.descriptorText ?? null;

  const selectId = `comparison-row-score-${dimensionId}`;
  const commentId = `comparison-row-comment-${dimensionId}`;
  const errorId = `comparison-row-comment-error-${dimensionId}`;

  return (
    <tr className={commentError ? 'comparison-row comparison-row-error' : 'comparison-row'}>
      <td className="comparison-row-dimension">
        <div className="comparison-row-dimension-name">{dimensionName}</div>
        {moderatedDescriptor && (
          <div className="comparison-row-dimension-descriptor">
            <strong>{strings.moderation.moderatedDescriptorPrefix(managerScore)}</strong> {moderatedDescriptor}
          </div>
        )}
      </td>

      <td className="comparison-row-cell comparison-row-cell-center">
        <span className={`level-badge level-badge-${selfScore}`}>{strings.assessment.levelAbbrev(selfScore)}</span>
      </td>

      <td className="comparison-row-cell comparison-row-cell-center">
        <label className="visually-hidden" htmlFor={selectId}>
          {strings.moderation.yourScoreLabel(dimensionName)}
        </label>
        <select
          id={selectId}
          className="comparison-row-select"
          value={managerScore}
          disabled={disabled}
          onChange={(event) => onManagerScoreChange(dimensionId, Number(event.target.value))}
        >
          {levels.map((level) => (
            <option key={level.level} value={level.level}>
              {strings.assessment.levelAbbrev(level.level)}
            </option>
          ))}
        </select>
      </td>

      <td className="comparison-row-cell comparison-row-cell-center">
        <DivergenceFlag selfScore={selfScore} managerScore={managerScore} />
      </td>

      <td className="comparison-row-cell">
        <label className="visually-hidden" htmlFor={commentId}>
          {strings.moderation.commentLabel(dimensionName)}
        </label>
        <textarea
          id={commentId}
          className={commentError ? 'comparison-row-comment comparison-row-comment-error' : 'comparison-row-comment'}
          value={comment}
          disabled={disabled}
          placeholder={
            commentRequired ? strings.moderation.commentPlaceholderRequired : strings.moderation.commentPlaceholderOptional
          }
          aria-invalid={commentError}
          aria-describedby={commentError ? errorId : undefined}
          onChange={(event) => onCommentChange(dimensionId, event.target.value)}
        />
        {commentError && (
          <p id={errorId} className="comparison-row-comment-error-text" role="alert">
            {strings.moderation.commentRequiredError}
          </p>
        )}
      </td>
    </tr>
  );
}
