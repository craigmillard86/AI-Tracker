import type { NodeAggregate } from '../../api/client';
import { DimensionBar } from '../DimensionBar/DimensionBar';
import { DivergenceFlag } from '../DivergenceFlag/DivergenceFlag';
import { SuppressedCell } from '../SuppressedCell/SuppressedCell';
import { strings } from '../../strings';

export interface EvidencePanelProps {
  /** The measured aggregate (HAP-11's rollup output; suppression-projected server-side). */
  measured: NodeAggregate;
  /** Server-computed measured floor level (FR-016/FR-047): the MODAL per-person floor level from the
   * measured aggregate's FloorLevelDistribution (ties broken toward the lower level) — null when
   * unavailable. */
  measuredFloorLevel: number | null;
  /** The latest declaration's level, or null when no declaration exists yet. */
  declaredLevel: number | null;
  /** declaredLevel - measuredFloorLevel, signed, server-computed (or recomputed client-side from the
   * same two already-received values — never a fresh figure). */
  declaredVsMeasuredDivergence: number | null;
}

/** Mean maturity for a figure-set = the average of its per-dimension means (0-3) — the same simple
 * arithmetic DashboardScreen (HAP-11) derives from the identical response field, not a new figure. */
function overallMean(perDimensionMean: Record<string, number>): number {
  const values = Object.values(perDimensionMean);
  if (values.length === 0) {
    return 0;
  }
  return values.reduce((a, b) => a + b, 0) / values.length;
}

/**
 * The declared-vs-measured evidence card (HAP-15; FR-047; DESIGN.md A8 "EvidencePanel" — DimensionBar
 * reuse, divergence rendered via DivergenceFlag + a plain-language sentence, never colour alone). Every
 * figure here is read straight from the caller's already-suppression-projected `NodeAggregate`/server
 * fields (F2/FR-071) — a suppressed measured aggregate renders NO numbers, ever.
 */
export function EvidencePanel({
  measured,
  measuredFloorLevel,
  declaredLevel,
  declaredVsMeasuredDivergence,
}: EvidencePanelProps): JSX.Element {
  if (measured.suppressed) {
    return (
      <div className="evidence-panel evidence-panel-suppressed">
        <h3 className="evidence-panel-title">{strings.buForms.evidenceTitle}</h3>
        <SuppressedCell variant="block" reason={measured.suppressionReason ?? 'N<4'} />
      </div>
    );
  }

  const figures = measured.figures!;
  const dimensions = [...measured.dimensions]
    .sort((a, b) => a.displayOrder - b.displayOrder)
    .map((d) => ({ key: d.key, name: d.name, mean: figures.perDimensionMean[d.key] ?? 0 }));
  const meanText = overallMean(figures.perDimensionMean).toFixed(1);

  return (
    <div className="evidence-panel">
      <h3 className="evidence-panel-title">{strings.buForms.evidenceTitle}</h3>

      {measuredFloorLevel !== null && (
        <div className="evidence-panel-callout">
          <p className="evidence-panel-floor">{strings.buForms.measuredFloorHeadline(measuredFloorLevel, meanText)}</p>
          <p className="evidence-panel-note">{strings.buForms.evidenceInfoNote}</p>
        </div>
      )}

      <ul className="evidence-panel-dims">
        {dimensions.map((d) => (
          <li key={d.key} className="evidence-panel-dim-row">
            <span className="evidence-panel-dim-name">{d.name}</span>
            <DimensionBar name={d.name} mean={d.mean} />
          </li>
        ))}
      </ul>

      {declaredLevel !== null && measuredFloorLevel !== null && (
        <div className="evidence-panel-divergence">
          <DivergenceFlag selfScore={declaredLevel} managerScore={measuredFloorLevel} />
          <p className="evidence-panel-divergence-sentence">
            {strings.buForms.divergenceSentence(
              declaredLevel,
              declaredVsMeasuredDivergence ?? declaredLevel - measuredFloorLevel,
            )}
          </p>
        </div>
      )}
    </div>
  );
}
