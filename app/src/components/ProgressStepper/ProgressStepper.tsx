import { strings } from '../../strings';

export interface ProgressStepperProps {
  /** Count of dimensions with a current-cycle self score (see AssessmentSelfScreen scoring
   * model — a prior-cycle pre-population does not itself count). */
  scored: number;
  total: number;
  /** Minimum self score across currently-scored dimensions; null when nothing is scored yet. */
  floorLevel: number | null;
}

/**
 * Dimension progress + projected floor level (DESIGN.md A8). The floor badge always prints the
 * level number — colour is reinforcement only. Progress is announced via a labelled `aria-live`
 * region so assistive tech hears updates as the user scores each dimension.
 */
export function ProgressStepper({ scored, total, floorLevel }: ProgressStepperProps): JSX.Element {
  const percent = total > 0 ? Math.round((scored / total) * 100) : 0;
  const remaining = Math.max(total - scored, 0);

  return (
    <div className="progress-stepper">
      <div className="progress-stepper-label">{strings.assessment.progressLabel}</div>

      <div className="progress-stepper-bar-row">
        <div className="progress-stepper-bar" aria-hidden="true">
          <div className="progress-stepper-bar-fill" style={{ width: `${percent}%` }} />
        </div>
        <span className="progress-stepper-count">
          <span className="progress-stepper-count-scored">{scored}</span>
          <span className="progress-stepper-count-sep" aria-hidden="true">
            /
          </span>
          <span className="progress-stepper-count-total">{total}</span>
        </span>
      </div>

      <p className="progress-stepper-hint">
        {remaining === 0 ? strings.assessment.progressHintComplete : strings.assessment.progressHintIncomplete(remaining)}
      </p>

      <div aria-live="polite" className="progress-stepper-announce">
        {strings.assessment.progressAnnouncement(scored, total)}
      </div>

      <div className="progress-stepper-floor">
        <div className="progress-stepper-floor-label">{strings.assessment.projectedFloorLabel}</div>
        {floorLevel === null ? (
          <div className="progress-stepper-floor-empty">{strings.assessment.notYetScored}</div>
        ) : (
          <div className="progress-stepper-floor-row">
            <span className={`level-badge level-badge-lg level-badge-${floorLevel}`}>
              {strings.assessment.levelAbbrev(floorLevel)}
            </span>
            <div className="progress-stepper-floor-caption">{strings.assessment.floorLevelLabel}</div>
          </div>
        )}
      </div>
    </div>
  );
}
