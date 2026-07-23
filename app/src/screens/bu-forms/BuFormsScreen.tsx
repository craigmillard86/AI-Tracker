import { useEffect, useMemo, useState } from 'react';
import {
  BuFormsWriteError,
  fetchBuDeclarations,
  fetchBuMetrics,
  fetchDashboard,
  postBuDeclaration,
  postBuMetrics,
  type BuDeclarationResponse,
  type BuDeclarationsResponse,
  type BuMonthlyMetricsRequest,
  type BuMonthlyMetricsResponse,
  type RagStatus,
} from '../../api/client';
import { DeclarationLevelPicker } from '../../components/DeclarationLevelPicker/DeclarationLevelPicker';
import { EvidencePanel } from '../../components/EvidencePanel/EvidencePanel';
import { strings } from '../../strings';
import { formatDate } from '../../utils/formatDate';

type LoadState = 'loading' | 'error' | 'ready' | 'noRole';

const RAG_OPTIONS: ReadonlyArray<RagStatus> = ['OnTrack', 'AtRisk', 'OffTrack'];

function pad2(n: number): string {
  return String(n).padStart(2, '0');
}

function toIsoDate(d: Date): string {
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`;
}

/** ISO date of the Monday of the current week — the weekly declaration's `weekOf` key (mockup's "due
 * Mondays" cadence). Not a form field; computed the same way each time the composer opens. */
function mondayOfCurrentWeekIso(): string {
  const now = new Date();
  const day = now.getDay(); // 0 = Sunday .. 6 = Saturday
  const diff = day === 0 ? -6 : 1 - day;
  return toIsoDate(new Date(now.getFullYear(), now.getMonth(), now.getDate() + diff));
}

/** ISO date of the first of the current month — the monthly metrics form's fixed month (mockup shows a
 * static "July 2026" tag, not a picker; the AC's literal v1 scope). */
function firstOfCurrentMonthIso(): string {
  const now = new Date();
  return toIsoDate(new Date(now.getFullYear(), now.getMonth(), 1));
}

/** `yyyy-MM-dd` -> `yyyy-MM` for the native `<input type="month">` value. */
function toMonthInputValue(iso: string): string {
  return iso.slice(0, 7);
}

function parseNumberOrNull(value: string): number | null {
  if (!value.trim()) {
    return null;
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function textOrNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed ? trimmed : null;
}

function mapWriteErrorText(
  error: unknown,
  forbiddenText: string,
  validationText: string,
  genericText: string,
): string {
  if (error instanceof BuFormsWriteError) {
    if (error.status === 403) {
      return forbiddenText;
    }
    if (error.status === 422) {
      return validationText;
    }
  }
  return genericText;
}

/**
 * BU capture forms (HAP-15; FR-047/FR-048; layout/IA per docs/design/mockups/bu-forms.html, binding).
 * Two cards: a weekly AI-DLC declaration composer beside the measured-evidence panel (`EvidencePanel`,
 * reused as-is per DESIGN.md A8), and a monthly Support/SOR metrics form with YTD carry-forward.
 *
 * BU resolution mirrors the rest of the app's "which BU is this caller's" convention (HAP-11's
 * `GET /api/me/dashboard`, `RollupReads.ResolveDefaultNodeAsync`): only a BU Lead / BU Delegate resolve
 * to a `Bu` node there, which is exactly this screen's write-authority scope — anyone else (including a
 * Group/Portfolio Leader with broader READ access to declarations elsewhere) sees the no-role empty
 * state here, not an error.
 */
export function BuFormsScreen(): JSX.Element {
  const [state, setState] = useState<LoadState>('loading');
  const [buId, setBuId] = useState<string | null>(null);
  const [declarations, setDeclarations] = useState<BuDeclarationsResponse | null>(null);
  const [metrics, setMetrics] = useState<BuMonthlyMetricsResponse | null>(null);

  const [declaredLevel, setDeclaredLevel] = useState<number | null>(null);
  const [nextLevelMonth, setNextLevelMonth] = useState('');
  const [ragChoice, setRagChoice] = useState<RagStatus>('OnTrack');
  const [note, setNote] = useState('');
  const [declSubmitting, setDeclSubmitting] = useState(false);
  const [declMessage, setDeclMessage] = useState<{ kind: 'success' | 'error'; text: string } | null>(null);

  const [timeSavingsPct, setTimeSavingsPct] = useState('');
  const [fewerPeopleNeeded, setFewerPeopleNeeded] = useState('');
  const [supportRatioImpact, setSupportRatioImpact] = useState('');
  const [customersYtd, setCustomersYtd] = useState('');
  const [ticketsYtd, setTicketsYtd] = useState('');
  const [resolvedByAiYtd, setResolvedByAiYtd] = useState('');
  const [aiAssistedYtd, setAiAssistedYtd] = useState('');
  const [sorCalledByOtherApps, setSorCalledByOtherApps] = useState('');
  const [metricsSubmitting, setMetricsSubmitting] = useState(false);
  const [metricsMessage, setMetricsMessage] = useState<{ kind: 'success' | 'error'; text: string } | null>(null);

  const month = useMemo(() => firstOfCurrentMonthIso(), []);

  useEffect(() => {
    let cancelled = false;

    fetchDashboard()
      .then((node) => {
        if (cancelled) {
          return null;
        }
        if (!node || node.nodeType !== 'Bu' || !node.nodeRef) {
          setState('noRole');
          return null;
        }
        setBuId(node.nodeRef);
        return Promise.all([fetchBuDeclarations(node.nodeRef), fetchBuMetrics(node.nodeRef, month)]);
      })
      .then((result) => {
        if (cancelled || !result) {
          return;
        }
        const [declResponse, metricsResponse] = result;
        setDeclarations(declResponse);
        setMetrics(metricsResponse);

        const latest = declResponse?.declarations[0] ?? null;
        if (latest) {
          setDeclaredLevel(latest.declaredLevel);
          setNextLevelMonth(latest.nextLevelExpectedDate ? toMonthInputValue(latest.nextLevelExpectedDate) : '');
          setRagChoice(latest.ragStatus);
          setNote(latest.note ?? '');
        }

        if (metricsResponse) {
          setTimeSavingsPct(
            metricsResponse.supportInternal.timeSavingsPct != null
              ? String(metricsResponse.supportInternal.timeSavingsPct)
              : '',
          );
          setFewerPeopleNeeded(metricsResponse.supportInternal.fewerPeopleNeeded ?? '');
          setSupportRatioImpact(metricsResponse.supportInternal.supportRatioImpact ?? '');
          setCustomersYtd(
            metricsResponse.supportCustomer.customersYtd != null
              ? String(metricsResponse.supportCustomer.customersYtd)
              : '',
          );
          setTicketsYtd(
            metricsResponse.supportCustomer.ticketsYtd != null ? String(metricsResponse.supportCustomer.ticketsYtd) : '',
          );
          setResolvedByAiYtd(
            metricsResponse.supportCustomer.resolvedByAiYtd != null
              ? String(metricsResponse.supportCustomer.resolvedByAiYtd)
              : '',
          );
          setAiAssistedYtd(
            metricsResponse.supportCustomer.aiAssistedYtd != null
              ? String(metricsResponse.supportCustomer.aiAssistedYtd)
              : '',
          );
          // SOR is current-month-only (FR-048) — the server never sends a value carried from a prior
          // month, so whatever it sent (including null on a fresh/carried-forward row) is rendered as-is.
          setSorCalledByOtherApps(metricsResponse.sorCalledByOtherApps ?? '');
        }

        setState('ready');
      })
      .catch(() => {
        if (!cancelled) {
          setState('error');
        }
      });

    return () => {
      cancelled = true;
    };
  }, [month]);

  const evidenceDivergence = useMemo(() => {
    if (!declarations) {
      return null;
    }
    if (declaredLevel !== null && declarations.measuredFloorLevel !== null) {
      return declaredLevel - declarations.measuredFloorLevel;
    }
    return declarations.declaredVsMeasuredDivergence;
  }, [declarations, declaredLevel]);

  async function handleSubmitDeclaration(): Promise<void> {
    if (!buId || declaredLevel === null) {
      return;
    }
    setDeclSubmitting(true);
    setDeclMessage(null);
    try {
      const weekOf = mondayOfCurrentWeekIso();
      const created: BuDeclarationResponse = await postBuDeclaration(buId, {
        weekOf,
        declaredLevel,
        nextLevelExpectedDate: nextLevelMonth ? `${nextLevelMonth}-01` : null,
        ragStatus: ragChoice,
        note: textOrNull(note),
      });
      setDeclarations((prev) => {
        if (!prev) {
          return prev;
        }
        const withoutSameWeek = prev.declarations.filter((d) => d.weekOf !== created.weekOf);
        return {
          ...prev,
          declarations: [created, ...withoutSameWeek],
          measuredFloorLevel: prev.measuredFloorLevel,
          declaredVsMeasuredDivergence:
            prev.measuredFloorLevel !== null ? created.declaredLevel - prev.measuredFloorLevel : prev.declaredVsMeasuredDivergence,
        };
      });
      setDeclMessage({ kind: 'success', text: strings.buForms.declarationSaveSuccess });
    } catch (error) {
      setDeclMessage({
        kind: 'error',
        text: mapWriteErrorText(
          error,
          strings.buForms.declarationSaveForbiddenError,
          strings.buForms.declarationSaveValidationError,
          strings.buForms.declarationSaveError,
        ),
      });
    } finally {
      setDeclSubmitting(false);
    }
  }

  async function handleSubmitMetrics(): Promise<void> {
    if (!buId) {
      return;
    }
    setMetricsSubmitting(true);
    setMetricsMessage(null);
    try {
      const body: BuMonthlyMetricsRequest = {
        month,
        supportInternal: {
          timeSavingsPct: parseNumberOrNull(timeSavingsPct),
          fewerPeopleNeeded: textOrNull(fewerPeopleNeeded),
          supportRatioImpact: textOrNull(supportRatioImpact),
        },
        supportCustomer: {
          customersYtd: parseNumberOrNull(customersYtd),
          ticketsYtd: parseNumberOrNull(ticketsYtd),
          resolvedByAiYtd: parseNumberOrNull(resolvedByAiYtd),
          aiAssistedYtd: parseNumberOrNull(aiAssistedYtd),
        },
        sorCalledByOtherApps: textOrNull(sorCalledByOtherApps),
      };
      const saved = await postBuMetrics(buId, body);
      setMetrics(saved);
      setMetricsMessage({ kind: 'success', text: strings.buForms.metricsSaveSuccess });
    } catch (error) {
      setMetricsMessage({
        kind: 'error',
        text: mapWriteErrorText(
          error,
          strings.buForms.metricsSaveForbiddenError,
          strings.buForms.metricsSaveValidationError,
          strings.buForms.metricsSaveError,
        ),
      });
    } finally {
      setMetricsSubmitting(false);
    }
  }

  if (state === 'loading') {
    return <p className="bu-forms-status">{strings.buForms.loading}</p>;
  }
  if (state === 'error') {
    return (
      <p className="bu-forms-status bu-forms-status-error" role="alert">
        {strings.buForms.loadError}
      </p>
    );
  }
  if (state === 'noRole') {
    return (
      <div className="bu-forms-empty">
        <div className="bu-forms-crumb">{strings.buForms.breadcrumb}</div>
        <h1 className="bu-forms-title">{strings.buForms.pageTitle}</h1>
        <p className="bu-forms-status">{strings.buForms.noRoleBody}</p>
      </div>
    );
  }

  return (
    <div className="bu-forms">
      <div className="bu-forms-head">
        <div className="bu-forms-crumb">{strings.buForms.breadcrumb}</div>
        <h1 className="bu-forms-title">{strings.buForms.pageTitle}</h1>
        <p className="bu-forms-subtitle">{strings.buForms.subtitle}</p>
      </div>

      <div className="bu-forms-grid">
        <section className="bu-forms-card">
          <div className="bu-forms-card-head">
            <h2>{strings.buForms.declarationCardTitle}</h2>
            <p className="bu-forms-hint">{strings.buForms.declarationCardHint}</p>
          </div>

          {declarations && (
            <EvidencePanel
              measured={declarations.measured}
              measuredFloorLevel={declarations.measuredFloorLevel}
              declaredLevel={declaredLevel}
              declaredVsMeasuredDivergence={evidenceDivergence}
            />
          )}

          <div className="bu-forms-field">
            <span className="bu-forms-field-label">{strings.buForms.declaredLevelFieldLabel}</span>
            <DeclarationLevelPicker groupName="bu-decl-level" value={declaredLevel} onSelect={setDeclaredLevel} />
          </div>

          <label className="bu-forms-field">
            <span>{strings.buForms.nextLevelDateLabel}</span>
            <input
              className="bu-forms-input"
              type="month"
              value={nextLevelMonth}
              onChange={(event) => setNextLevelMonth(event.target.value)}
            />
          </label>

          <fieldset className="bu-forms-rag-group">
            <legend className="visually-hidden">{strings.buForms.ragFieldLabel}</legend>
            <span className="bu-forms-field-label" aria-hidden="true">
              {strings.buForms.ragFieldLabel}
            </span>
            <div className="bu-forms-rag-options">
              {RAG_OPTIONS.map((option) => (
                <label
                  key={option}
                  className={`bu-forms-rag-option${ragChoice === option ? ' bu-forms-rag-option-selected' : ''}`}
                >
                  <input
                    type="radio"
                    name="bu-decl-rag"
                    value={option}
                    checked={ragChoice === option}
                    onChange={() => setRagChoice(option)}
                  />
                  {strings.buForms.ragLabels[option]}
                </label>
              ))}
            </div>
          </fieldset>

          <label className="bu-forms-field">
            <span>{strings.buForms.noteLabel}</span>
            <textarea
              className="bu-forms-input"
              placeholder={strings.buForms.notePlaceholder}
              value={note}
              onChange={(event) => setNote(event.target.value)}
            />
          </label>

          <button
            type="button"
            className="bu-forms-btn bu-forms-btn-primary bu-forms-btn-block"
            onClick={handleSubmitDeclaration}
            disabled={declSubmitting || declaredLevel === null}
          >
            {strings.buForms.saveDeclarationButton}
          </button>

          {declMessage && (
            <p
              className={`bu-forms-status ${
                declMessage.kind === 'error' ? 'bu-forms-status-error' : 'bu-forms-status-success'
              }`}
              role={declMessage.kind === 'error' ? 'alert' : 'status'}
            >
              {declMessage.text}
            </p>
          )}

          <div className="bu-forms-history">
            <h3 className="bu-forms-history-title">{strings.buForms.historyTitle}</h3>
            {declarations && declarations.declarations.length > 0 ? (
              <ul className="bu-forms-history-list">
                {declarations.declarations.map((d) => (
                  <li key={d.id} className="bu-forms-history-row">
                    <span className={`level-badge level-badge-${d.declaredLevel}`}>
                      {strings.assessment.levelAbbrev(d.declaredLevel)}
                    </span>
                    <span>{formatDate(d.weekOf)}</span>
                    <span>{strings.buForms.ragLabels[d.ragStatus]}</span>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="bu-forms-status">{strings.buForms.historyEmpty}</p>
            )}
          </div>
        </section>

        <section className="bu-forms-card">
          <div className="bu-forms-card-head">
            <h2>{strings.buForms.metricsCardTitle}</h2>
            <p className="bu-forms-hint">{strings.buForms.metricsCardHint}</p>
          </div>

          <fieldset className="bu-forms-formgroup">
            <legend className="bu-forms-grouptitle">{strings.buForms.supportInternalLegend}</legend>
            <div className="bu-forms-field">
              <label>
                <span>{strings.buForms.timeSavingsLabel}</span>
                <input
                  className="bu-forms-input"
                  type="number"
                  value={timeSavingsPct}
                  onChange={(event) => setTimeSavingsPct(event.target.value)}
                />
              </label>
              <span className="bu-forms-field-help">{strings.buForms.timeSavingsHint}</span>
            </div>
            <div className="bu-forms-two">
              <label className="bu-forms-field">
                <span>{strings.buForms.fewerPeopleLabel}</span>
                <select
                  className="bu-forms-input"
                  value={fewerPeopleNeeded}
                  onChange={(event) => setFewerPeopleNeeded(event.target.value)}
                >
                  <option value="">{strings.buForms.fewerPeopleUnselected}</option>
                  {strings.buForms.fewerPeopleOptions.map((option) => (
                    <option key={option} value={option}>
                      {option}
                    </option>
                  ))}
                </select>
              </label>
              <label className="bu-forms-field">
                <span>{strings.buForms.supportRatioLabel}</span>
                <input
                  className="bu-forms-input"
                  value={supportRatioImpact}
                  onChange={(event) => setSupportRatioImpact(event.target.value)}
                />
              </label>
            </div>
          </fieldset>

          <fieldset className="bu-forms-formgroup">
            <legend className="bu-forms-grouptitle">{strings.buForms.supportCustomerLegend}</legend>
            {metrics?.carriedForward && <p className="bu-forms-carry-hint">{strings.buForms.carriedForwardHint}</p>}
            <div className="bu-forms-two">
              <label className="bu-forms-field">
                <span>{strings.buForms.customersYtdLabel}</span>
                <input
                  className="bu-forms-input"
                  type="number"
                  value={customersYtd}
                  onChange={(event) => setCustomersYtd(event.target.value)}
                />
              </label>
              <label className="bu-forms-field">
                <span>{strings.buForms.ticketsYtdLabel}</span>
                <input
                  className="bu-forms-input"
                  type="number"
                  value={ticketsYtd}
                  onChange={(event) => setTicketsYtd(event.target.value)}
                />
              </label>
              <label className="bu-forms-field">
                <span>{strings.buForms.resolvedByAiYtdLabel}</span>
                <input
                  className="bu-forms-input"
                  type="number"
                  value={resolvedByAiYtd}
                  onChange={(event) => setResolvedByAiYtd(event.target.value)}
                />
              </label>
              <label className="bu-forms-field">
                <span>{strings.buForms.aiAssistedYtdLabel}</span>
                <input
                  className="bu-forms-input"
                  type="number"
                  value={aiAssistedYtd}
                  onChange={(event) => setAiAssistedYtd(event.target.value)}
                />
              </label>
            </div>
          </fieldset>

          <fieldset className="bu-forms-formgroup">
            <legend className="bu-forms-grouptitle">{strings.buForms.sorLegend}</legend>
            <div className="bu-forms-field">
              <label>
                <span>{strings.buForms.sorFieldLabel}</span>
                <input
                  className="bu-forms-input"
                  value={sorCalledByOtherApps}
                  onChange={(event) => setSorCalledByOtherApps(event.target.value)}
                />
              </label>
              <span className="bu-forms-field-help">{strings.buForms.sorFieldHint}</span>
            </div>
          </fieldset>

          <button
            type="button"
            className="bu-forms-btn bu-forms-btn-secondary bu-forms-btn-block"
            onClick={handleSubmitMetrics}
            disabled={metricsSubmitting}
          >
            {strings.buForms.saveMetricsButton}
          </button>

          {metricsMessage && (
            <p
              className={`bu-forms-status ${
                metricsMessage.kind === 'error' ? 'bu-forms-status-error' : 'bu-forms-status-success'
              }`}
              role={metricsMessage.kind === 'error' ? 'alert' : 'status'}
            >
              {metricsMessage.text}
            </p>
          )}
        </section>
      </div>
    </div>
  );
}
