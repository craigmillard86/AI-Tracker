import { useEffect, useMemo, useState } from 'react';
import {
  AssessmentWriteError,
  fetchSelfAssessment,
  saveSelfAssessmentScores,
  submitSelfAssessment,
  type SelfAssessmentResponse,
} from '../../api/client';
import { LevelSelectorCard } from '../../components/LevelSelectorCard/LevelSelectorCard';
import { ProgressStepper } from '../../components/ProgressStepper/ProgressStepper';
import { PurposeBanner } from '../../components/PurposeBanner/PurposeBanner';
import { strings } from '../../strings';

type LoadState = 'loading' | 'error' | 'noCycle' | 'ready';

interface DimensionEdit {
  /** The CURRENT-cycle self score, or null until the person picks a level this cycle. A
   * pre-populated prior-cycle value is shown (see `effectiveLevel` below) but does not itself
   * set this — see the story's binding scoring/progress model. */
  selfScore: number | null;
  evidence: string;
}

type SaveStatus = 'idle' | 'saving' | 'saved' | 'error';
type SubmitStatus = 'idle' | 'submitting' | 'error';

function initialEdits(data: SelfAssessmentResponse): Record<string, DimensionEdit> {
  const edits: Record<string, DimensionEdit> = {};
  for (const dimension of data.dimensions) {
    edits[dimension.dimensionId] = {
      selfScore: dimension.selfScore,
      evidence: dimension.selfEvidence ?? '',
    };
  }
  return edits;
}

function mapSaveError(error: unknown): string {
  if (error instanceof AssessmentWriteError) {
    if (error.status === 423) {
      return strings.assessment.saveDraftLocked;
    }
    if (error.status === 409) {
      return strings.assessment.saveDraftAlreadySubmitted;
    }
  }
  return strings.assessment.saveDraftError;
}

function mapSubmitError(error: unknown): string {
  if (error instanceof AssessmentWriteError) {
    if (error.status === 422) {
      return strings.assessment.submitIncomplete;
    }
    if (error.status === 409) {
      return strings.assessment.submitAlreadySubmitted;
    }
    if (error.status === 423) {
      return strings.assessment.submitLocked;
    }
  }
  return strings.assessment.submitError;
}

/**
 * The self-assessment form (HAP-8; FR-007/062/066). One dimension per section, pre-populated
 * from the prior cycle, with a sticky progress panel — layout/IA per
 * `docs/design/mockups/assessment-self.html` (binding).
 *
 * Binding scoring/progress model (story notes): `priorScore` pre-selects a card (with the "last
 * month" pill) but a dimension only counts toward "x of N" / the projected floor once it carries
 * a CURRENT self score this cycle — this is exactly the mockup's 5-of-7 incomplete state, where a
 * prior-populated dimension can still read "to do". Effective displayed selection is
 * `selfScore ?? priorScore`.
 */
export function AssessmentSelfScreen(): JSX.Element {
  const [loadState, setLoadState] = useState<LoadState>('loading');
  const [data, setData] = useState<SelfAssessmentResponse | null>(null);
  const [edits, setEdits] = useState<Record<string, DimensionEdit>>({});
  const [submitted, setSubmitted] = useState(false);
  const [saveStatus, setSaveStatus] = useState<SaveStatus>('idle');
  const [saveErrorMessage, setSaveErrorMessage] = useState<string | null>(null);
  const [submitStatus, setSubmitStatus] = useState<SubmitStatus>('idle');
  const [submitErrorMessage, setSubmitErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetchSelfAssessment()
      .then((response) => {
        if (cancelled) {
          return;
        }
        if (response === null) {
          setLoadState('noCycle');
          return;
        }
        setData(response);
        setEdits(initialEdits(response));
        setSubmitted(response.submitted);
        setLoadState('ready');
      })
      .catch(() => {
        if (!cancelled) {
          setLoadState('error');
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const dimensions = useMemo(
    () => (data ? [...data.dimensions].sort((a, b) => a.displayOrder - b.displayOrder) : []),
    [data],
  );

  const scored = dimensions.filter((dimension) => edits[dimension.dimensionId]?.selfScore != null).length;
  const floorLevel = dimensions.reduce<number | null>((floor, dimension) => {
    const score = edits[dimension.dimensionId]?.selfScore;
    if (score == null) {
      return floor;
    }
    return floor === null ? score : Math.min(floor, score);
  }, null);
  const canSubmit = dimensions.length > 0 && scored === dimensions.length;
  // Read-only when the server says the cycle no longer accepts this caller's writes (closed without a
  // late override). The submitted state has its own view below, so this only applies to an open form.
  const readOnly = data != null && !data.editable && !submitted;

  function handleSelect(dimensionId: string, level: number): void {
    setEdits((prev) => ({
      ...prev,
      [dimensionId]: { selfScore: level, evidence: prev[dimensionId]?.evidence ?? '' },
    }));
  }

  function handleEvidenceChange(dimensionId: string, evidence: string): void {
    setEdits((prev) => ({
      ...prev,
      [dimensionId]: { selfScore: prev[dimensionId]?.selfScore ?? null, evidence },
    }));
  }

  async function handleSaveDraft(): Promise<void> {
    setSaveStatus('saving');
    setSaveErrorMessage(null);
    const scores = dimensions
      .filter((dimension) => edits[dimension.dimensionId]?.selfScore != null)
      .map((dimension) => ({
        dimensionId: dimension.dimensionId,
        score: edits[dimension.dimensionId].selfScore as number,
        evidence: edits[dimension.dimensionId].evidence || null,
      }));
    try {
      await saveSelfAssessmentScores(scores);
      setSaveStatus('saved');
    } catch (error) {
      setSaveStatus('error');
      setSaveErrorMessage(mapSaveError(error));
    }
  }

  async function handleSubmit(): Promise<void> {
    if (!canSubmit) {
      return;
    }
    setSubmitStatus('submitting');
    setSubmitErrorMessage(null);
    try {
      await submitSelfAssessment();
      setSubmitted(true);
      setSubmitStatus('idle');
    } catch (error) {
      setSubmitStatus('error');
      setSubmitErrorMessage(mapSubmitError(error));
    }
  }

  return (
    <div className="assessment-screen">
      <h1 className="assessment-title">{strings.assessment.pageTitle}</h1>

      {loadState === 'loading' && <p className="assessment-status">{strings.assessment.loading}</p>}

      {loadState === 'error' && (
        <p className="assessment-status assessment-status-error" role="alert">
          {strings.assessment.loadError}
        </p>
      )}

      {loadState === 'noCycle' && (
        <div className="assessment-empty">
          <h2 className="assessment-empty-title">{strings.assessment.noOpenCycleTitle}</h2>
          <p className="assessment-empty-body">{strings.assessment.noOpenCycleBody}</p>
        </div>
      )}

      {loadState === 'ready' && submitted && (
        <div className="assessment-submitted" role="status">
          <h2 className="assessment-submitted-title">{strings.assessment.submittedTitle}</h2>
          <p className="assessment-submitted-body">{strings.assessment.submittedBody}</p>
        </div>
      )}

      {loadState === 'ready' && !submitted && (
        <div className="assessment-layout">
          <div className="assessment-main">
            <PurposeBanner />

            {readOnly && (
              <p className="assessment-status assessment-status-readonly" role="status">
                {strings.assessment.readOnlyNotice}
              </p>
            )}

            {dimensions.map((dimension, index) => {
              const edit = edits[dimension.dimensionId] ?? { selfScore: null, evidence: '' };
              const effectiveLevel = edit.selfScore ?? dimension.priorScore;
              return (
                <section className="assessment-dimension-card" key={dimension.dimensionId}>
                  <div className="assessment-dimension-head">
                    <div className="assessment-dimension-eyebrow">
                      {strings.assessment.dimensionOfTotal(index + 1, dimensions.length)}
                    </div>
                    <h2 className="assessment-dimension-name">{dimension.name}</h2>
                  </div>

                  <fieldset className="level-set">
                    <legend className="visually-hidden">
                      {strings.assessment.levelSetLegend(dimension.name)}
                    </legend>
                    {dimension.levels.map((level) => (
                      <LevelSelectorCard
                        key={level.level}
                        groupName={`dimension-${dimension.dimensionId}`}
                        level={level.level}
                        levelName={level.levelName}
                        descriptorText={level.descriptorText}
                        checked={effectiveLevel === level.level}
                        isPrior={dimension.priorScore === level.level}
                        disabled={readOnly}
                        onSelect={(chosen) => handleSelect(dimension.dimensionId, chosen)}
                      />
                    ))}
                  </fieldset>

                  <label className="assessment-evidence-field">
                    <span>{strings.assessment.evidenceLabel}</span>
                    <textarea
                      className="assessment-evidence-input"
                      placeholder={strings.assessment.evidencePlaceholder}
                      value={edit.evidence}
                      disabled={readOnly}
                      onChange={(event) => handleEvidenceChange(dimension.dimensionId, event.target.value)}
                    />
                    <span className="assessment-evidence-hint">{strings.assessment.evidenceHint}</span>
                  </label>
                </section>
              );
            })}

            <div className="assessment-actions">
              <p className="assessment-actions-hint">{strings.assessment.submitHint}</p>
              <div className="assessment-actions-buttons">
                <button
                  type="button"
                  className="assessment-btn assessment-btn-secondary"
                  onClick={handleSaveDraft}
                  disabled={readOnly || saveStatus === 'saving'}
                >
                  {strings.assessment.saveDraft}
                </button>
                <button
                  type="button"
                  className="assessment-btn assessment-btn-primary"
                  onClick={handleSubmit}
                  disabled={readOnly || !canSubmit || submitStatus === 'submitting'}
                >
                  {strings.assessment.submitForReview}
                </button>
              </div>
            </div>

            {saveStatus === 'saved' && (
              <p className="assessment-status assessment-status-success" role="status">
                {strings.assessment.saveDraftSuccess}
              </p>
            )}
            {saveStatus === 'error' && saveErrorMessage && (
              <p className="assessment-status assessment-status-error" role="alert">
                {saveErrorMessage}
              </p>
            )}
            {submitStatus === 'error' && submitErrorMessage && (
              <p className="assessment-status assessment-status-error" role="alert">
                {submitErrorMessage}
              </p>
            )}
          </div>

          <aside className="assessment-aside">
            <div className="assessment-aside-panel">
              <ProgressStepper scored={scored} total={dimensions.length} floorLevel={floorLevel} />
            </div>
          </aside>
        </div>
      )}
    </div>
  );
}
