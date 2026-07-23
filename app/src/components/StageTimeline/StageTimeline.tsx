import type { StageHistoryEntry } from '../../api/client';
import { strings } from '../../strings';
import { formatDate } from '../../utils/formatDate';

export interface StageTimelineProps {
  /** Oldest -> newest (matches `InitiativeDetailResponse.stageHistory`); the last entry is the
   * current stage and gets the "current" visual treatment. */
  entries: StageHistoryEntry[];
}

/**
 * Forward-only stage-history timeline (DESIGN.md A8 StageTimeline; register-detail; FR-028).
 * Ordered-list semantics, brand-teal current node, border-gray connectors. The real data model
 * carries only stage + date per entry (no note field) — no interactive states; history is
 * immutable. The current (last) node's status is carried in text (`currentStageTag`), never by
 * colour alone (DESIGN.md A5 colour independence).
 */
export function StageTimeline({ entries }: StageTimelineProps): JSX.Element {
  return (
    <ol className="stage-timeline">
      {entries.map((entry, index) => {
        const isCurrent = index === entries.length - 1;
        return (
          <li
            key={entry.id}
            className={`stage-timeline-item${isCurrent ? ' stage-timeline-item-current' : ''}`}
          >
            <span className="stage-timeline-track">
              <span className="stage-timeline-dot" aria-hidden="true" />
            </span>
            <span className="stage-timeline-content">
              <span className="stage-timeline-label">
                {strings.register.stageLabels[entry.stage]}
                {isCurrent && (
                  <span className="stage-timeline-current-tag">{strings.registerDetail.currentStageTag}</span>
                )}
              </span>
              <span className="stage-timeline-date">{formatDate(entry.enteredAt)}</span>
            </span>
          </li>
        );
      })}
    </ol>
  );
}
