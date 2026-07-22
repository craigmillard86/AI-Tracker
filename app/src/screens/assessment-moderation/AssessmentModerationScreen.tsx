import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  AssessmentWriteError,
  fetchTeamMemberAssessment,
  fetchTeamReviews,
  submitModeration,
  type MemberAssessmentResponse,
  type ModerationDecision,
  type TeamReviewsResponse,
} from '../../api/client';
import { isCommentRequired } from '../../components/ComparisonRow/commentRequired';
import { ComparisonRow } from '../../components/ComparisonRow/ComparisonRow';
import { strings } from '../../strings';

type QueueLoadState = 'loading' | 'error' | 'ready';
type MemberLoadState = 'idle' | 'loading' | 'error' | 'ready';
type SaveStatus = 'idle' | 'saving' | 'success' | 'error';

interface DecisionEdit {
  managerScore: number;
  comment: string;
}

function initialDecisions(member: MemberAssessmentResponse): Record<string, DecisionEdit> {
  const decisions: Record<string, DecisionEdit> = {};
  for (const dimension of member.dimensions) {
    decisions[dimension.dimensionId] = {
      managerScore: dimension.managerScore ?? dimension.defaultManagerScore,
      comment: dimension.managerComment ?? '',
    };
  }
  return decisions;
}

function mapModerationError(error: unknown): string {
  if (error instanceof AssessmentWriteError) {
    if (error.status === 423) {
      return strings.moderation.saveLocked;
    }
    if (error.status === 409) {
      return strings.moderation.saveStateError;
    }
    if (error.status === 422) {
      return strings.moderation.saveValidationError;
    }
  }
  return strings.moderation.saveError;
}

/**
 * Manager Review (HAP-9; FR-008/009/010/011/063/069). One direct report's self-assessment
 * moderated dimension by dimension, with a sticky review-queue aside — layout/IA per
 * `docs/design/mockups/assessment-moderation.html` (binding).
 *
 * The write API (`PUT /api/team/reviews/{assessmentId}`) is the single moderation endpoint —
 * dimensions omitted from its `decisions` payload are defaulted server-side (carry-forward/adopt,
 * FR-063). "Carry forward unchanged dimensions" here controls whether dimensions left at their
 * carry-forward default are actually sent (letting the server apply FR-063) or always sent
 * explicitly. "Save & next" and "Submit moderation" both call that same write; "Save & next"
 * additionally advances the queue to the next report once the write succeeds.
 */
export function AssessmentModerationScreen(): JSX.Element {
  const [queueLoadState, setQueueLoadState] = useState<QueueLoadState>('loading');
  const [teamData, setTeamData] = useState<TeamReviewsResponse | null>(null);
  const [selectedPersonId, setSelectedPersonId] = useState<string | null>(null);
  const [memberLoadState, setMemberLoadState] = useState<MemberLoadState>('idle');
  const [member, setMember] = useState<MemberAssessmentResponse | null>(null);
  const [decisions, setDecisions] = useState<Record<string, DecisionEdit>>({});
  const [carryForwardUnchanged, setCarryForwardUnchanged] = useState(true);
  const [saveStatus, setSaveStatus] = useState<SaveStatus>('idle');
  const [saveErrorMessage, setSaveErrorMessage] = useState<string | null>(null);

  const selectReport = useCallback((personId: string): void => {
    setSelectedPersonId(personId);
    setMemberLoadState('loading');
    setMember(null);
    setDecisions({});
    setCarryForwardUnchanged(true);
    setSaveStatus('idle');
    setSaveErrorMessage(null);
    fetchTeamMemberAssessment(personId)
      .then((response) => {
        if (response === null) {
          setMemberLoadState('error');
          return;
        }
        setMember(response);
        setDecisions(initialDecisions(response));
        setMemberLoadState('ready');
      })
      .catch(() => {
        setMemberLoadState('error');
      });
  }, []);

  useEffect(() => {
    let cancelled = false;
    fetchTeamReviews()
      .then((response) => {
        if (cancelled) {
          return;
        }
        setTeamData(response);
        setQueueLoadState('ready');
        const firstSelectable = response.reviews.find((item) => item.canModerate && item.assessmentId != null);
        if (firstSelectable) {
          selectReport(firstSelectable.personId);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setQueueLoadState('error');
        }
      });
    return () => {
      cancelled = true;
    };
    // Runs once on mount only — selectReport is stable (useCallback, empty deps).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const dimensions = useMemo(
    () => (member ? [...member.dimensions].sort((a, b) => a.displayOrder - b.displayOrder) : []),
    [member],
  );

  const readOnly = member != null && !member.editable;
  // A successful "Submit moderation" (saveStatus 'success', not advancing to the next report) also
  // forces the row into a read-only posture immediately — a client-side guard alongside the
  // post-submit refetch above, so a stray second click can never fire a write against an
  // assessment this screen already knows was just successfully moderated (mirrors HAP-8's
  // closed/submitted read-only treatment).
  const effectiveReadOnly = readOnly || saveStatus === 'success';

  // Uses the same `isCommentRequired` rule ComparisonRow renders with — a single source of truth so
  // an untouched carry-forward default that itself diverges >= commentThreshold
  // (dimension.defaultCommentRequired) blocks submit exactly as a manager-edited divergence does.
  // Never accept-all-defaults into a submit that would then 422 (FR-009/FR-063 consistency). The
  // threshold itself is server-owned (`member.commentThreshold`) — never a client literal.
  const forcedCommentDimensions = useMemo(
    () =>
      dimensions.filter((dimension) => {
        const edit = decisions[dimension.dimensionId];
        if (!edit || member == null) {
          return false;
        }
        const required = isCommentRequired(
          dimension.selfScore,
          edit.managerScore,
          member.commentThreshold,
          dimension.defaultManagerScore,
          dimension.defaultCommentRequired,
        );
        return required && edit.comment.trim().length === 0;
      }),
    [dimensions, decisions, member],
  );

  const canSubmit = dimensions.length > 0 && forcedCommentDimensions.length === 0 && !readOnly;

  const calibration = useMemo(() => {
    if (dimensions.length === 0) {
      return null;
    }
    let deltaSum = 0;
    let scoreSum = 0;
    let floor: number | null = null;
    for (const dimension of dimensions) {
      const edit = decisions[dimension.dimensionId];
      const managerScore = edit ? edit.managerScore : dimension.defaultManagerScore;
      deltaSum += Math.abs(dimension.selfScore - managerScore);
      scoreSum += managerScore;
      floor = floor === null ? managerScore : Math.min(floor, managerScore);
    }
    return {
      meanDelta: deltaSum / dimensions.length,
      floor,
      mean: scoreSum / dimensions.length,
    };
  }, [dimensions, decisions]);

  function handleManagerScoreChange(dimensionId: string, level: number): void {
    setDecisions((prev) => ({
      ...prev,
      [dimensionId]: { managerScore: level, comment: prev[dimensionId]?.comment ?? '' },
    }));
  }

  function handleCommentChange(dimensionId: string, comment: string): void {
    setDecisions((prev) => ({
      ...prev,
      [dimensionId]: { managerScore: prev[dimensionId]?.managerScore ?? 0, comment },
    }));
  }

  function nextSelectableReport(excludingPersonId: string, source: TeamReviewsResponse): string | null {
    const next = source.reviews.find(
      (item) => item.personId !== excludingPersonId && item.canModerate && item.assessmentId != null,
    );
    return next ? next.personId : null;
  }

  async function handleSave(advance: boolean): Promise<void> {
    if (!member || !canSubmit) {
      return;
    }
    setSaveStatus('saving');
    setSaveErrorMessage(null);

    const decisionsPayload: ModerationDecision[] = dimensions
      .filter((dimension) => {
        if (!carryForwardUnchanged) {
          return true;
        }
        const edit = decisions[dimension.dimensionId];
        const unchanged =
          edit.managerScore === dimension.defaultManagerScore && edit.comment === (dimension.managerComment ?? '');
        return !unchanged;
      })
      .map((dimension) => ({
        dimensionId: dimension.dimensionId,
        managerScore: decisions[dimension.dimensionId].managerScore,
        comment: decisions[dimension.dimensionId].comment || null,
      }));

    try {
      await submitModeration(member.assessmentId, decisionsPayload);
      setSaveStatus('success');

      const refreshed = await fetchTeamReviews().catch(() => null);
      if (refreshed) {
        setTeamData(refreshed);
      }

      if (advance) {
        const next = refreshed ? nextSelectableReport(member.personId, refreshed) : null;
        if (next) {
          selectReport(next);
        } else {
          setSelectedPersonId(null);
          setMember(null);
          setDecisions({});
          setMemberLoadState('idle');
        }
      } else {
        // Stay on this report but reflect that it is now moderated/read-only (mirrors
        // AssessmentSelfScreen's closed/submitted read-only treatment, HAP-8) — refetch so the
        // card shows the server's own state=Moderated/editable=false and the score-of-record
        // values just written. `saveStatus === 'success'` (below, in the render) is an additional
        // client-side guard so a stray second click is blocked even if this refetch fails.
        const refreshedMember = await fetchTeamMemberAssessment(member.personId).catch(() => null);
        if (refreshedMember) {
          setMember(refreshedMember);
          setDecisions(initialDecisions(refreshedMember));
        }
      }
    } catch (error) {
      setSaveStatus('error');
      setSaveErrorMessage(mapModerationError(error));
    }
  }

  const moderatedCount = teamData
    ? teamData.reviews.filter((item) => item.state === 'Moderated' || item.state === 'AutoAdopted').length
    : 0;
  const leftCount = teamData
    ? teamData.reviews.filter((item) => item.state !== 'Moderated' && item.state !== 'AutoAdopted').length
    : 0;

  return (
    <div className="moderation-screen">
      <div className="moderation-crumb">{strings.moderation.breadcrumb}</div>
      <h1 className="moderation-title">{strings.moderation.pageTitle}</h1>
      {teamData && <p className="moderation-subtitle">{strings.moderation.subtitle(teamData.cycleName)}</p>}

      {queueLoadState === 'loading' && <p className="moderation-status">{strings.moderation.loading}</p>}

      {queueLoadState === 'error' && (
        <p className="moderation-status moderation-status-error" role="alert">
          {strings.moderation.loadError}
        </p>
      )}

      {queueLoadState === 'ready' && teamData && !teamData.isManager && (
        <div className="moderation-empty">
          <h2 className="moderation-empty-title">{strings.moderation.notManagerTitle}</h2>
          <p className="moderation-empty-body">{strings.moderation.notManagerBody}</p>
        </div>
      )}

      {queueLoadState === 'ready' && teamData && teamData.isManager && teamData.reviews.length === 0 && (
        <div className="moderation-empty">
          <h2 className="moderation-empty-title">{strings.moderation.noReportsTitle}</h2>
          <p className="moderation-empty-body">{strings.moderation.noReportsBody}</p>
        </div>
      )}

      {queueLoadState === 'ready' && teamData && teamData.isManager && teamData.reviews.length > 0 && (
        <div className="moderation-layout">
          <div className="moderation-main">
            {memberLoadState === 'loading' && <p className="moderation-status">{strings.moderation.memberLoading}</p>}

            {memberLoadState === 'error' && (
              <p className="moderation-status moderation-status-error" role="alert">
                {strings.moderation.memberLoadError}
              </p>
            )}

            {memberLoadState === 'idle' && (
              <div className="moderation-empty">
                <p className="moderation-empty-body">
                  {teamData.reviews.some((item) => item.canModerate && item.assessmentId != null)
                    ? strings.moderation.selectReportPrompt
                    : strings.moderation.queueComplete}
                </p>
              </div>
            )}

            {memberLoadState === 'ready' && member && (
              <div className="moderation-card">
                <div className="moderation-card-head">
                  <div>
                    <h2 className="moderation-card-name">
                      {member.displayName}
                      {member.onLeave && <span className="moderation-onleave-tag">{strings.moderation.onLeaveTag}</span>}
                    </h2>
                    <p className="moderation-card-hint">{strings.moderation.reportingToYou}</p>
                  </div>
                  <label className="moderation-carry-toggle">
                    <input
                      type="checkbox"
                      checked={carryForwardUnchanged}
                      disabled={effectiveReadOnly}
                      onChange={(event) => setCarryForwardUnchanged(event.target.checked)}
                    />
                    {strings.moderation.carryForwardToggle}
                  </label>
                </div>

                {effectiveReadOnly && (
                  <p className="moderation-status moderation-status-readonly" role="status">
                    {strings.moderation.readOnlyNotice}
                  </p>
                )}

                {forcedCommentDimensions.length > 0 && (
                  <div className="moderation-callout" role="alert">
                    {strings.moderation.forcedCommentCallout(forcedCommentDimensions.length)}
                  </div>
                )}

                <table className="moderation-table">
                  <thead>
                    <tr>
                      <th className="moderation-table-dimension-col">{strings.moderation.dimensionHeader}</th>
                      <th className="moderation-table-center-col">{strings.moderation.selfHeader}</th>
                      <th className="moderation-table-center-col">{strings.moderation.yourScoreHeader}</th>
                      <th className="moderation-table-center-col">{strings.moderation.divergenceHeader}</th>
                      <th className="moderation-table-dimension-col">{strings.moderation.commentHeader}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {dimensions.map((dimension) => {
                      const edit = decisions[dimension.dimensionId] ?? {
                        managerScore: dimension.defaultManagerScore,
                        comment: '',
                      };
                      return (
                        <ComparisonRow
                          key={dimension.dimensionId}
                          dimensionId={dimension.dimensionId}
                          dimensionName={dimension.name}
                          levels={dimension.levels}
                          selfScore={dimension.selfScore}
                          managerScore={edit.managerScore}
                          comment={edit.comment}
                          commentThreshold={member.commentThreshold}
                          defaultManagerScore={dimension.defaultManagerScore}
                          defaultCommentRequired={dimension.defaultCommentRequired}
                          disabled={effectiveReadOnly}
                          onManagerScoreChange={handleManagerScoreChange}
                          onCommentChange={handleCommentChange}
                        />
                      );
                    })}
                  </tbody>
                </table>

                <div className="moderation-actions">
                  {calibration && (
                    <p className="moderation-actions-hint">
                      <span className="moderation-stat">
                        <span className="moderation-stat-label">{strings.moderation.calibrationDeltaLabel}</span>
                        <strong>{calibration.meanDelta.toFixed(2)}</strong>
                      </span>
                      <span className="moderation-stat">
                        <span className="moderation-stat-label">{strings.moderation.moderatedFloorLabel}</span>
                        {calibration.floor !== null && (
                          <span className={`level-badge level-badge-${calibration.floor}`}>
                            {strings.assessment.levelAbbrev(calibration.floor)}
                          </span>
                        )}
                      </span>
                      <span className="moderation-stat">
                        <span className="moderation-stat-label">{strings.moderation.meanLabel}</span>
                        <strong>{calibration.mean.toFixed(2)}</strong>
                      </span>
                    </p>
                  )}
                  <div className="moderation-actions-buttons">
                    <button
                      type="button"
                      className="moderation-btn moderation-btn-secondary"
                      onClick={() => handleSave(true)}
                      disabled={effectiveReadOnly || !canSubmit || saveStatus === 'saving'}
                    >
                      {strings.moderation.saveAndNext}
                    </button>
                    <button
                      type="button"
                      className="moderation-btn moderation-btn-primary"
                      onClick={() => handleSave(false)}
                      disabled={effectiveReadOnly || !canSubmit || saveStatus === 'saving'}
                    >
                      {strings.moderation.submitModeration}
                    </button>
                  </div>
                </div>

                {saveStatus === 'success' && (
                  <p className="moderation-status moderation-status-success" role="status">
                    {strings.moderation.moderationComplete}
                  </p>
                )}
                {saveStatus === 'error' && saveErrorMessage && (
                  <p className="moderation-status moderation-status-error" role="alert">
                    {saveErrorMessage}
                  </p>
                )}
              </div>
            )}
          </div>

          <aside className="moderation-aside">
            <div className="moderation-aside-panel">
              <div className="moderation-queue-head">
                <span className="moderation-queue-title">{strings.moderation.queueTitle}</span>
                <span className="moderation-queue-left">{strings.moderation.queueLeft(leftCount)}</span>
              </div>

              <ul className="moderation-queue-list">
                {teamData.reviews.map((item) => {
                  const isDone = item.state === 'Moderated' || item.state === 'AutoAdopted';
                  const isReviewing = item.personId === selectedPersonId;
                  const selectable = item.assessmentId != null;
                  let stateLabel: string = strings.moderation.queueStateToDo;
                  let stateClassName = 'moderation-queue-state-todo';
                  if (isDone) {
                    stateLabel = strings.moderation.queueStateDone;
                    stateClassName = 'moderation-queue-state-done';
                  } else if (isReviewing) {
                    stateLabel = strings.moderation.queueStateReviewing;
                    stateClassName = 'moderation-queue-state-reviewing';
                  }
                  return (
                    <li key={item.personId}>
                      <button
                        type="button"
                        className={isReviewing ? 'moderation-queue-item moderation-queue-item-active' : 'moderation-queue-item'}
                        disabled={!selectable}
                        onClick={() => selectReport(item.personId)}
                      >
                        <span>{item.displayName}</span>
                        <span className={`moderation-queue-state ${stateClassName}`}>{stateLabel}</span>
                      </button>
                    </li>
                  );
                })}
              </ul>

              <p className="moderation-queue-footer">
                {strings.moderation.queueFooter(teamData.reviews.length, moderatedCount)}
              </p>
            </div>
          </aside>
        </div>
      )}
    </div>
  );
}
