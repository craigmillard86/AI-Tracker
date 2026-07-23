import { useState } from 'react';
import type { NrLine } from '../../api/client';
import { strings } from '../../strings';

/** The add-line form draft handed to `onAdd` — matches the `POST .../nr-lines` request body minus
 * the initiative id (the caller already knows which initiative it's editing). */
export interface NrLineDraft {
  year: number;
  direction: 'Direct' | 'Indirect';
  recurrence: 'OneTime' | 'Recurring';
  amountUsd: number;
  description?: string;
}

export interface NRLineEditorProps {
  lines: NrLine[];
  /** Caller's write authority for this initiative (`InitiativeDetailResponse.canEdit`) — when false,
   * the add-line form and per-row delete buttons are not rendered at all (not just disabled), and an
   * explanatory note is shown instead. */
  editable: boolean;
  onAdd: (line: NrLineDraft) => void | Promise<void>;
  onDelete: (lineId: string) => void | Promise<void>;
}

const DIRECTIONS: ReadonlyArray<'Direct' | 'Indirect'> = ['Direct', 'Indirect'];
const RECURRENCES: ReadonlyArray<'OneTime' | 'Recurring'> = ['OneTime', 'Recurring'];

function formatUsd(amount: number): string {
  return `$${amount.toLocaleString('en-US')}`;
}

/**
 * NR-line add/remove table (DESIGN.md A8 NRLineEditor; register-detail). Table layout, form inputs
 * at 8px radius, a tertiary text "add line" button. Row delete is a labelled button (never icon-only);
 * locked rows (submission-referenced) show a labelled lock indicator + tooltip text — the label text is
 * always what's printed, never colour/icon-only (DESIGN.md A5/A7).
 */
export function NRLineEditor({ lines, editable, onAdd, onDelete }: NRLineEditorProps): JSX.Element {
  const currentYear = new Date().getFullYear();
  const [year, setYear] = useState(currentYear);
  const [direction, setDirection] = useState<'Direct' | 'Indirect'>('Direct');
  const [recurrence, setRecurrence] = useState<'OneTime' | 'Recurring'>('Recurring');
  const [amountUsd, setAmountUsd] = useState('');
  const [description, setDescription] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const total = lines.reduce((sum, line) => sum + line.amountUsd, 0);
  const amountValue = Number(amountUsd);
  const amountIsValid = amountUsd.trim() !== '' && Number.isFinite(amountValue);

  async function handleAdd(): Promise<void> {
    if (!amountIsValid) {
      return;
    }
    setSubmitting(true);
    try {
      await onAdd({
        year,
        direction,
        recurrence,
        amountUsd: amountValue,
        description: description.trim() || undefined,
      });
      setAmountUsd('');
      setDescription('');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="nr-editor">
      {lines.length === 0 ? (
        <p className="nr-empty">{strings.registerDetail.nrEmpty}</p>
      ) : (
        <table className="nr-table">
          <thead>
            <tr>
              <th scope="col">{strings.registerDetail.nrColType}</th>
              <th scope="col">{strings.registerDetail.nrColCadence}</th>
              <th scope="col" className="nr-col-num">
                {strings.registerDetail.nrColAmount}
              </th>
              <th scope="col">{strings.registerDetail.nrColDescription}</th>
              {editable && (
                <th scope="col" className="nr-col-center">
                  {strings.registerDetail.nrColActions}
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => (
              <tr key={line.id}>
                <td>
                  <b>{strings.registerDetail.directionLabels[line.direction]}</b>
                </td>
                <td>{strings.registerDetail.recurrenceLabels[line.recurrence]}</td>
                <td className="nr-col-num">{formatUsd(line.amountUsd)}</td>
                <td className="nr-muted">{line.description ?? strings.register.emptyValue}</td>
                {editable && (
                  <td className="nr-col-center">
                    {line.locked ? (
                      <span className="nr-locked" title={strings.registerDetail.nrLockedTooltip}>
                        {strings.registerDetail.nrLockedLabel}
                      </span>
                    ) : (
                      <button
                        type="button"
                        className="nr-delete-btn"
                        onClick={() => onDelete(line.id)}
                      >
                        {strings.registerDetail.nrDeleteLabel}
                      </button>
                    )}
                  </td>
                )}
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="nr-total-row">
              <td colSpan={2}>
                <b>{strings.registerDetail.nrTotalLabel}</b>
              </td>
              <td className="nr-col-num">
                <b>{formatUsd(total)}</b>
              </td>
              <td colSpan={editable ? 2 : 1} />
            </tr>
          </tfoot>
        </table>
      )}

      {editable ? (
        <div className="nr-add-form">
          <div className="nr-add-fields">
            <label className="nr-field">
              <span>{strings.registerDetail.nrYearLabel}</span>
              <input
                className="nr-input"
                type="number"
                value={year}
                onChange={(event) => setYear(Number(event.target.value))}
              />
            </label>
            <label className="nr-field">
              <span>{strings.registerDetail.nrDirectionLabel}</span>
              <select
                className="nr-input"
                value={direction}
                onChange={(event) => setDirection(event.target.value as 'Direct' | 'Indirect')}
              >
                {DIRECTIONS.map((option) => (
                  <option key={option} value={option}>
                    {strings.registerDetail.directionLabels[option]}
                  </option>
                ))}
              </select>
            </label>
            <label className="nr-field">
              <span>{strings.registerDetail.nrRecurrenceLabel}</span>
              <select
                className="nr-input"
                value={recurrence}
                onChange={(event) => setRecurrence(event.target.value as 'OneTime' | 'Recurring')}
              >
                {RECURRENCES.map((option) => (
                  <option key={option} value={option}>
                    {strings.registerDetail.recurrenceLabels[option]}
                  </option>
                ))}
              </select>
            </label>
            <label className="nr-field">
              <span>{strings.registerDetail.nrAmountLabel}</span>
              <input
                className="nr-input"
                type="number"
                value={amountUsd}
                onChange={(event) => setAmountUsd(event.target.value)}
              />
            </label>
            <label className="nr-field nr-field-grow">
              <span>{strings.registerDetail.nrDescriptionLabel}</span>
              <input
                className="nr-input"
                type="text"
                placeholder={strings.registerDetail.nrDescriptionPlaceholder}
                value={description}
                onChange={(event) => setDescription(event.target.value)}
              />
            </label>
          </div>
          <button
            type="button"
            className="nr-add-btn"
            onClick={handleAdd}
            disabled={submitting || !amountIsValid}
          >
            {strings.registerDetail.nrAddLine}
          </button>
        </div>
      ) : (
        <p className="nr-readonly-note">{strings.registerDetail.notEditableNote}</p>
      )}
    </div>
  );
}
