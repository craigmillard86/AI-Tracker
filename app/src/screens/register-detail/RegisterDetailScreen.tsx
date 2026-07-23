import { useEffect, useMemo, useState } from 'react';
import {
  InitiativeWriteError,
  createNrLine,
  deleteNrLine,
  fetchBusinessUnits,
  fetchHarrisCategories,
  fetchInitiativeDetail,
  postWeeklyUpdate,
  type BusinessUnitResponse,
  type HarrisCategoryResponse,
  type InitiativeDetailResponse,
  type InitiativeStage,
  type RagStatus,
  type WeeklyUpdateRequest,
} from '../../api/client';
import { LevelBadge } from '../../components/LevelBadge/LevelBadge';
import { NRLineEditor, type NrLineDraft } from '../../components/NRLineEditor/NRLineEditor';
import { RagChip } from '../../components/RagChip/RagChip';
import { StageTimeline } from '../../components/StageTimeline/StageTimeline';
import { StaleRowFlag } from '../../components/StaleRowFlag/StaleRowFlag';
import { strings } from '../../strings';
import { formatDate } from '../../utils/formatDate';

type RefState = 'loading' | 'error' | 'notFound' | 'ready';

export interface RegisterDetailScreenProps {
  initiativeId: string;
  onBack: () => void;
}

const RAG_OPTIONS: ReadonlyArray<RagStatus> = ['OnTrack', 'AtRisk', 'OffTrack'];

/** Stages the overdue banner applies to (data-model.md InitiativeWeeklyUpdate: "Overdue = active
 * stage (Evaluation→Scaled) and no update in 7 days"; root spec §4.2 exempts Idea and Retired). */
const OVERDUE_ELIGIBLE_STAGES: ReadonlyArray<InitiativeStage> = ['Evaluation', 'Pilot', 'Production', 'Scaled'];

/** Whole days between an ISO timestamp and now (never negative in practice; clamped at 0 for
 * display) — mirrors RegisterListScreen's helper (HAP-13). */
function daysSince(iso: string): number {
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) {
    return 0;
  }
  return Math.max(0, Math.floor((Date.now() - then) / (1000 * 60 * 60 * 24)));
}

/** Governance chip colour mapping. There is no dedicated "governance chip" in the DESIGN.md A8
 * component inventory, so this reuses the RagChip tinted-pill visual pattern locally (`.gov-chip`,
 * this screen's own CSS — tokens only). Mapping is a reasonable judgement call, not framework
 * content: PHI/PII/High-risk/Clinical-style states -> red; Med/Internal/conditional/standalone-style
 * states -> amber; everything else (Low/None/on-Cogito/human-in-the-loop-style) -> green. The LABEL
 * text is always what's printed — colour is reinforcement only (DESIGN.md A2/A5). */
function governanceVariant(value: string): 'green' | 'amber' | 'red' {
  const v = value.toLowerCase();
  if (v.includes('phi') || v.includes('pii') || v.includes('high') || v.includes('clinical')) {
    return 'red';
  }
  if (v.includes('med') || v.includes('internal') || v.includes('conditional') || v.includes('standalone')) {
    return 'amber';
  }
  return 'green';
}

function GovChip({ label }: { label: string }): JSX.Element {
  const variant = governanceVariant(label);
  return <span className={`gov-chip gov-chip-${variant}`}>{label}</span>;
}

function mapUpdateError(error: unknown): string {
  if (error instanceof InitiativeWriteError && error.status === 422) {
    return strings.registerDetail.updateValidationError;
  }
  return strings.registerDetail.updateError;
}

/**
 * The AI initiative detail screen (HAP-14; FR-028; layout/IA per
 * docs/design/mockups/register-detail.html, binding). Identity/tech card, NR capture card
 * (`NRLineEditor`), governance & risk card (informational only, §4.2), a weekly-update composer, a
 * `StageTimeline`, and a recent-updates feed. Every write control (weekly update, NR line add/delete)
 * is gated on the caller's `canEdit` for this initiative — hidden with an explanatory note when false,
 * never left to fail silently against the server.
 */
export function RegisterDetailScreen({ initiativeId, onBack }: RegisterDetailScreenProps): JSX.Element {
  const [refState, setRefState] = useState<RefState>('loading');
  const [detail, setDetail] = useState<InitiativeDetailResponse | null>(null);
  const [businessUnits, setBusinessUnits] = useState<BusinessUnitResponse[]>([]);
  const [categories, setCategories] = useState<HarrisCategoryResponse[]>([]);

  const [ragChoice, setRagChoice] = useState<RagStatus>('OnTrack');
  const [note, setNote] = useState('');
  const [customers, setCustomers] = useState('');
  const [updateSubmitting, setUpdateSubmitting] = useState(false);
  const [updateMessage, setUpdateMessage] = useState<{ kind: 'success' | 'error'; text: string } | null>(null);
  const [nrError, setNrError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setRefState('loading');
    Promise.all([fetchInitiativeDetail(initiativeId), fetchBusinessUnits(), fetchHarrisCategories()])
      .then(([initiativeDetail, units, cats]) => {
        if (cancelled) {
          return;
        }
        if (initiativeDetail === null) {
          setRefState('notFound');
          return;
        }
        setDetail(initiativeDetail);
        setBusinessUnits(units);
        setCategories(cats);
        setRagChoice(initiativeDetail.ragStatus);
        setCustomers(
          initiativeDetail.customersInProduction != null ? String(initiativeDetail.customersInProduction) : '',
        );
        setRefState('ready');
      })
      .catch(() => {
        if (!cancelled) {
          setRefState('error');
        }
      });
    return () => {
      cancelled = true;
    };
  }, [initiativeId]);

  const buName = useMemo(
    () => businessUnits.find((unit) => unit.id === detail?.businessUnitId)?.name ?? strings.register.unknownBu,
    [businessUnits, detail],
  );
  const categoryName = useMemo(
    () => categories.find((category) => category.id === detail?.categoryId)?.name ?? strings.register.unknownCategory,
    [categories, detail],
  );

  if (refState === 'loading') {
    return <p className="register-detail-status">{strings.registerDetail.loading}</p>;
  }
  if (refState === 'error') {
    return (
      <p className="register-detail-status register-detail-status-error" role="alert">
        {strings.registerDetail.loadError}
      </p>
    );
  }
  if (refState === 'notFound' || !detail) {
    return (
      <div className="register-detail-notfound">
        <h1 className="register-detail-title">{strings.registerDetail.notFoundTitle}</h1>
        <p className="register-detail-status">{strings.registerDetail.notFoundBody}</p>
        <button type="button" className="register-detail-btn register-detail-btn-secondary" onClick={onBack}>
          {strings.registerDetail.backButton}
        </button>
      </div>
    );
  }

  const days = daysSince(detail.lastUpdateAt);
  const overdue = days > 7 && OVERDUE_ELIGIBLE_STAGES.includes(detail.currentStage);
  const registeredDate = formatDate(detail.registeredAt);

  async function handlePostUpdate(): Promise<void> {
    if (!detail) {
      return;
    }
    setUpdateSubmitting(true);
    setUpdateMessage(null);
    try {
      const body: WeeklyUpdateRequest = { ragStatus: ragChoice };
      if (note.trim()) {
        body.note = note.trim();
      }
      if (detail.categoryCustomerDeployed && customers.trim()) {
        const parsed = Number(customers);
        if (Number.isFinite(parsed)) {
          body.customersInProduction = parsed;
        }
      }
      const created = await postWeeklyUpdate(detail.id, body);
      setDetail((prev) =>
        prev
          ? {
              ...prev,
              updates: [created, ...prev.updates],
              ragStatus: created.ragStatus,
              lastUpdateAt: created.createdAt,
            }
          : prev,
      );
      setNote('');
      setUpdateMessage({ kind: 'success', text: strings.registerDetail.updateSuccess });
    } catch (error) {
      setUpdateMessage({ kind: 'error', text: mapUpdateError(error) });
    } finally {
      setUpdateSubmitting(false);
    }
  }

  async function handleAddNrLine(line: NrLineDraft): Promise<void> {
    if (!detail) {
      return;
    }
    setNrError(null);
    try {
      const created = await createNrLine(detail.id, line);
      setDetail((prev) => (prev ? { ...prev, nrLines: [...prev.nrLines, created] } : prev));
    } catch {
      setNrError(strings.registerDetail.nrAddError);
    }
  }

  async function handleDeleteNrLine(lineId: string): Promise<void> {
    if (!detail) {
      return;
    }
    setNrError(null);
    try {
      await deleteNrLine(detail.id, lineId);
      setDetail((prev) => (prev ? { ...prev, nrLines: prev.nrLines.filter((line) => line.id !== lineId) } : prev));
    } catch (error) {
      if (error instanceof InitiativeWriteError && error.status === 409) {
        setNrError(strings.registerDetail.nrDeleteLockedError);
      } else {
        setNrError(strings.registerDetail.nrDeleteError);
      }
    }
  }

  return (
    <div className="register-detail">
      <div className="register-detail-head">
        <div>
          <div className="register-detail-crumb">{strings.registerDetail.breadcrumb}</div>
          <h1 className="register-detail-title">{detail.name}</h1>
          <p className="register-detail-subtitle">
            {strings.registerDetail.subtitle(buName, categoryName, registeredDate)}
          </p>
        </div>
        <div className="register-detail-actions">
          <button type="button" className="register-detail-btn register-detail-btn-secondary" onClick={onBack}>
            {strings.registerDetail.backButton}
          </button>
          {/* Edit-governance form is a later story — HAP-14 ships read + weekly update + NR lines only.
              Inert CTA, matching RegisterListScreen's own inert "New initiative" pattern. */}
          <button type="button" className="register-detail-btn register-detail-btn-primary" disabled>
            {strings.registerDetail.editButton}
          </button>
        </div>
      </div>

      <div className="register-detail-grid">
        <div className="register-detail-main">
          <section className="register-detail-card">
            <div className="register-detail-card-head">
              <div>
                <h2>{detail.name}</h2>
                <p className="register-detail-hint">
                  {strings.registerDetail.subtitle(buName, categoryName, registeredDate)}
                </p>
              </div>
              <div className="register-detail-chips">
                <LevelBadge level={detail.aiDlcLevel} />
                <RagChip status={detail.ragStatus} />
                <StaleRowFlag days={days} />
              </div>
            </div>

            <div className="register-detail-two-col">
              <div>
                <div className="register-detail-label">{strings.registerDetail.sponsorLabel}</div>
                <p className="register-detail-value">{detail.sponsorPersonId ?? strings.register.emptyValue}</p>
                <div className="register-detail-label">{strings.registerDetail.ownerLabel}</div>
                <p className="register-detail-value">{detail.ownerPersonId}</p>
                <div className="register-detail-label">{strings.registerDetail.dimensionsLabel}</div>
                <p className="register-detail-value">
                  {detail.dimensionsAdvanced.length > 0
                    ? detail.dimensionsAdvanced.map((dimension) => (
                        <span key={dimension} className="register-detail-tag">
                          {dimension}
                        </span>
                      ))
                    : strings.register.emptyValue}
                </p>
              </div>
              <div>
                <div className="register-detail-label">{strings.registerDetail.technologyLabel}</div>
                <p className="register-detail-value">
                  {[...detail.modelsProviders, ...detail.vendorsTools].join(' · ') || strings.register.emptyValue}
                </p>
                {detail.categoryCustomerDeployed && (
                  <>
                    <div className="register-detail-label">{strings.registerDetail.customersLabel}</div>
                    <p className="register-detail-value">
                      {detail.customersInProduction != null
                        ? strings.registerDetail.customersValue(detail.customersInProduction)
                        : strings.register.emptyValue}
                    </p>
                  </>
                )}
                <div className="register-detail-label">{strings.registerDetail.descriptionLabel}</div>
                <p className="register-detail-value register-detail-muted">
                  {detail.description ?? strings.register.emptyValue}
                </p>
              </div>
            </div>
          </section>

          <section className="register-detail-card">
            <h2>{strings.registerDetail.nrTitle}</h2>
            <p className="register-detail-hint register-detail-hint-spaced">{strings.registerDetail.nrHint}</p>
            <NRLineEditor
              lines={detail.nrLines}
              editable={detail.canEdit}
              onAdd={handleAddNrLine}
              onDelete={handleDeleteNrLine}
            />
            {nrError && (
              <p className="register-detail-status register-detail-status-error" role="alert">
                {nrError}
              </p>
            )}
          </section>

          <section className="register-detail-card">
            <div className="register-detail-card-head">
              <div>
                <h2>{strings.registerDetail.governanceTitle}</h2>
                <p className="register-detail-hint">{strings.registerDetail.governanceHint}</p>
              </div>
            </div>
            <table className="register-detail-gov-table">
              <tbody>
                <tr>
                  <td>{strings.registerDetail.dataSensitivityLabel}</td>
                  <td>
                    <GovChip label={detail.dataSensitivity} />
                  </td>
                </tr>
                <tr>
                  <td>{strings.registerDetail.regulatoryLabel}</td>
                  <td>
                    <GovChip
                      label={
                        detail.regulatoryRelevance.length > 0
                          ? detail.regulatoryRelevance.join(' · ')
                          : strings.register.emptyValue
                      }
                    />
                  </td>
                </tr>
                <tr>
                  <td>{strings.registerDetail.riskTierLabel}</td>
                  <td>
                    <GovChip label={strings.registerDetail.riskTierLabels[detail.riskTier]} />
                  </td>
                </tr>
                <tr>
                  <td>{strings.registerDetail.approvalLabel}</td>
                  <td>
                    <GovChip label={detail.approvalStatus ?? strings.register.emptyValue} />
                  </td>
                </tr>
                <tr>
                  <td>{strings.registerDetail.oversightLabel}</td>
                  <td>
                    <GovChip label={detail.oversightModel ?? strings.register.emptyValue} />
                  </td>
                </tr>
                <tr>
                  <td>{strings.registerDetail.platformLabel}</td>
                  <td>
                    <GovChip
                      label={
                        detail.usesCogito
                          ? strings.registerDetail.platformCogito
                          : strings.registerDetail.platformStandalone
                      }
                    />
                  </td>
                </tr>
              </tbody>
            </table>
          </section>
        </div>

        <aside className="register-detail-aside">
          <section className="register-detail-card">
            <h2>{strings.registerDetail.updateTitle}</h2>
            <p className="register-detail-hint register-detail-hint-spaced">{strings.registerDetail.updateHint}</p>

            {overdue && (
              <div className="register-detail-callout" role="alert">
                {strings.registerDetail.overdueCallout(days)}
              </div>
            )}

            {!detail.canEdit ? (
              <p className="register-detail-status">{strings.registerDetail.notEditableNote}</p>
            ) : (
              <>
                <fieldset className="register-detail-rag-group">
                  <legend className="visually-hidden">{strings.registerDetail.ragFieldLabel}</legend>
                  <span className="register-detail-field-label" aria-hidden="true">
                    {strings.registerDetail.ragFieldLabel}
                  </span>
                  <div className="register-detail-rag-options">
                    {RAG_OPTIONS.map((option) => (
                      <label
                        key={option}
                        className={`register-detail-rag-option${
                          ragChoice === option ? ' register-detail-rag-option-selected' : ''
                        }`}
                      >
                        <input
                          type="radio"
                          name="weekly-rag"
                          value={option}
                          checked={ragChoice === option}
                          onChange={() => setRagChoice(option)}
                        />
                        {strings.register.ragLabels[option]}
                      </label>
                    ))}
                  </div>
                </fieldset>

                <label className="register-detail-field">
                  <span>{strings.registerDetail.noteLabel}</span>
                  <textarea
                    className="register-detail-input"
                    placeholder={strings.registerDetail.notePlaceholder}
                    value={note}
                    onChange={(event) => setNote(event.target.value)}
                  />
                </label>

                {detail.categoryCustomerDeployed && (
                  <label className="register-detail-field">
                    <span>{strings.registerDetail.customersFieldLabel}</span>
                    <input
                      className="register-detail-input"
                      type="number"
                      value={customers}
                      onChange={(event) => setCustomers(event.target.value)}
                    />
                  </label>
                )}

                <button
                  type="button"
                  className="register-detail-btn register-detail-btn-primary register-detail-btn-block"
                  onClick={handlePostUpdate}
                  disabled={updateSubmitting}
                >
                  {strings.registerDetail.postUpdateButton}
                </button>

                {updateMessage && (
                  <p
                    className={`register-detail-status ${
                      updateMessage.kind === 'error'
                        ? 'register-detail-status-error'
                        : 'register-detail-status-success'
                    }`}
                    role={updateMessage.kind === 'error' ? 'alert' : 'status'}
                  >
                    {updateMessage.text}
                  </p>
                )}
              </>
            )}
          </section>

          <section className="register-detail-card">
            <h2>{strings.registerDetail.stageHistoryTitle}</h2>
            <StageTimeline entries={detail.stageHistory} />
          </section>

          <section className="register-detail-card">
            <h2>{strings.registerDetail.recentUpdatesTitle}</h2>
            {detail.updates.length === 0 ? (
              <p className="register-detail-status">{strings.registerDetail.recentUpdatesEmpty}</p>
            ) : (
              <ul className="register-detail-updates">
                {detail.updates.map((update) => (
                  <li key={update.id} className="register-detail-update-row">
                    <div className="register-detail-update-head">
                      <RagChip status={update.ragStatus} />
                      <span className="register-detail-hint">
                        {strings.registerDetail.updateMeta(formatDate(update.createdAt), update.createdBy)}
                      </span>
                    </div>
                    {update.note && <p className="register-detail-update-note">{update.note}</p>}
                  </li>
                ))}
              </ul>
            )}
          </section>
        </aside>
      </div>
    </div>
  );
}
