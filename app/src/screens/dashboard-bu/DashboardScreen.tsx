import { useEffect, useMemo, useState } from 'react';
import { fetchDashboard, type AggregateFigures, type NodeAggregate } from '../../api/client';
import { DimensionBar } from '../../components/DimensionBar/DimensionBar';
import { levelOfMean } from '../../components/DimensionBar/level';
import { StatTile } from '../../components/StatTile/StatTile';
import { SuppressedCell } from '../../components/SuppressedCell/SuppressedCell';
import { TrendSparkline } from '../../components/TrendSparkline/TrendSparkline';
import { strings } from '../../strings';

type LoadState = 'loading' | 'error' | 'ready' | 'empty';

export interface DashboardScreenProps {
  /** Wires the page's single primary CTA to the existing self-assessment flow (shell view switch). */
  onStartSelfAssessment?: () => void;
}

/** Mean maturity for a figure-set = the average of its per-dimension means (0–3). */
function meanMaturity(figures: AggregateFigures): number {
  const values = Object.values(figures.perDimensionMean);
  if (values.length === 0) {
    return 0;
  }
  return values.reduce((a, b) => a + b, 0) / values.length;
}

/**
 * Floor-level facts from the per-person floor distribution (FR-016/018), NOT from rounded means (BB1). The
 * headline floor is the weakest level anyone is at (the true minimum), and the breakdown surfaces the "% at
 * L0" tail — so a half-L0 team reads honestly as Floor L0, never rounded up to L1.
 */
function floorFacts(figures: AggregateFigures): { level: number; pctZero: number; pctAbove: number } {
  const dist = figures.floorLevelDistribution;
  const total = Object.values(dist).reduce((a, b) => a + b, 0);
  const present = Object.keys(dist)
    .filter((k) => (dist[k] ?? 0) > 0)
    .map((k) => Number(k));
  const level = present.length > 0 ? Math.min(...present) : 0;
  const zero = dist['0'] ?? 0;
  const pctZero = total > 0 ? Math.round((zero / total) * 100) : 0;
  return { level, pctZero, pctAbove: 100 - pctZero };
}

/**
 * The maturity dashboard (DESIGN.md A6/A8; layout/IA per docs/design/mockups/dashboard-bu.html, binding).
 * Renders any {@link NodeAggregate} — floor distribution, per-dimension bars, cross-cycle trend, honest
 * suppression, the FR-041 initiative stubs.
 *
 * Data source: `GET /api/me/dashboard` (BB2) — resolves the caller's DEFAULT node server-side (Exec →
 * all-HIG, Portfolio/Group Leader → their node, BU Lead → their BU, else → own team) and scope-checks it,
 * so each persona reaches their own scope from the router-less shell with one call. The BU-scoped
 * (`fetchBuDashboard`) and org-scoped (`fetchOrgRollup`) endpoints are exposed in the client for an
 * org-tree / BU-picker entry point in a later story (deferred; see the story notes), which renders this
 * same component unchanged. Every figure is suppression-projected server-side (F2/FR-071): a suppressed
 * node carries NO numbers and the suppressed state renders instead.
 */
export function DashboardScreen({ onStartSelfAssessment }: DashboardScreenProps = {}): JSX.Element {
  const [state, setState] = useState<LoadState>('loading');
  const [node, setNode] = useState<NodeAggregate | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetchDashboard()
      .then((response) => {
        if (cancelled) {
          return;
        }
        if (response === null) {
          setState('empty');
          return;
        }
        setNode(response);
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
  }, []);

  const dimensions = useMemo(() => {
    if (!node || node.suppressed || !node.figures) {
      return [];
    }
    const means = node.figures.perDimensionMean;
    return [...node.dimensions]
      .sort((a, b) => a.displayOrder - b.displayOrder)
      .map((d) => ({ key: d.key, name: d.name, mean: means[d.key] ?? 0 }));
  }, [node]);

  // Cross-cycle mean-maturity series (published closed cycles, oldest → newest) + the current cycle.
  const trendMeans = useMemo(() => {
    if (!node || node.suppressed || !node.figures) {
      return [] as number[];
    }
    const history = node.trend
      .filter((p) => !p.suppressed && p.figures)
      .map((p) => meanMaturity(p.figures!));
    return [...history, meanMaturity(node.figures)];
  }, [node]);

  if (state === 'loading') {
    return <p className="dashboard-status">{strings.dashboard.loading}</p>;
  }
  if (state === 'error') {
    return (
      <p className="dashboard-status dashboard-status-error" role="alert">
        {strings.dashboard.loadError}
      </p>
    );
  }
  if (state === 'empty' || !node) {
    return (
      <div className="dashboard-empty">
        <h1 className="dashboard-title">{strings.dashboard.noDataTitle}</h1>
        <p className="dashboard-status">{strings.dashboard.noDataBody}</p>
      </div>
    );
  }

  return (
    <div className="dashboard">
      <div className="dashboard-head">
        <div>
          <div className="dashboard-crumb">{strings.dashboard.breadcrumb}</div>
          <h1 className="dashboard-title">{node.nodeName}</h1>
          <p className="dashboard-subtitle">{strings.dashboard.subtitle(node.nodeName, node.cycleName)}</p>
          <p className="dashboard-freshness">{node.live ? strings.dashboard.liveTag : strings.dashboard.snapshotTag}</p>
        </div>
        {onStartSelfAssessment && (
          <button type="button" className="dashboard-cta" onClick={onStartSelfAssessment}>
            {strings.dashboard.startSelfAssessment}
          </button>
        )}
      </div>

      {node.suppressed ? (
        <div className="dashboard-suppressed">
          <h2 className="dashboard-card-title">{strings.dashboard.suppressedTitle}</h2>
          <SuppressedCell variant="block" reason={node.suppressionReason ?? 'N<4'} />
          <p className="dashboard-status">
            {strings.dashboard.suppressedReasonText(node.suppressionReason ?? 'N<4')}
          </p>
        </div>
      ) : (
        <PublishedDashboard node={node} dimensions={dimensions} trendMeans={trendMeans} />
      )}
    </div>
  );
}

interface PublishedProps {
  node: NodeAggregate;
  dimensions: Array<{ key: string; name: string; mean: number }>;
  trendMeans: number[];
}

function PublishedDashboard({ node, dimensions, trendMeans }: PublishedProps): JSX.Element {
  const figures = node.figures!;
  const currentMean = trendMeans.length > 0 ? trendMeans[trendMeans.length - 1] : 0;
  const floor = floorFacts(figures);
  const completionPct = Math.round(figures.completionPct * 100);
  const unmoderatedPct = Math.round(figures.unmoderatedPct * 100);

  // Trend arrow vs the previous cycle (the second-to-last point in the series).
  const trend = useMemo(() => {
    if (trendMeans.length < 2) {
      return { direction: 'flat' as const, text: strings.dashboard.trendNone };
    }
    const prev = trendMeans[trendMeans.length - 2];
    const delta = currentMean - prev;
    const deltaText = Math.abs(delta).toFixed(1);
    if (delta > 0.05) {
      return { direction: 'up' as const, text: strings.dashboard.trendUp(deltaText) };
    }
    if (delta < -0.05) {
      return { direction: 'down' as const, text: strings.dashboard.trendDown(deltaText) };
    }
    return { direction: 'flat' as const, text: strings.dashboard.trendFlat };
  }, [trendMeans, currentMean]);

  // Weakest dimension for the maturity-gap callout.
  const weakest = dimensions.reduce<{ name: string; mean: number } | null>(
    (min, d) => (min === null || d.mean < min.mean ? { name: d.name, mean: d.mean } : min),
    null,
  );

  const sparklineLabel = `${strings.dashboard.sparklineLabelPrefix} ${trendMeans.map((m) => m.toFixed(1)).join(', ')}`;

  return (
    <>
      <div className="dashboard-kpis">
        <StatTile
          label={strings.dashboard.floorLevelLabel}
          value={<span className={`maturity-badge maturity-badge-${floor.level}`}>{strings.assessment.levelAbbrev(floor.level)}</span>}
          sub={
            <>
              {strings.dashboard.floorLevelHint}
              <br />
              <span className="dashboard-floor-breakdown">
                {strings.dashboard.floorBreakdown(floor.pctAbove, floor.pctZero)}
              </span>
            </>
          }
        />
        <StatTile
          label={strings.dashboard.meanMaturityLabel}
          value={currentMean.toFixed(1)}
          trend={trend}
          aside={
            trendMeans.length >= 2 ? (
              <span className="dashboard-sparkline-wrap">
                <span className="visually-hidden">{sparklineLabel}</span>
                <TrendSparkline values={trendMeans} />
              </span>
            ) : undefined
          }
          sub={strings.dashboard.meanMaturityHint}
        />
        <StatTile
          label={strings.dashboard.completionLabel}
          value={`${completionPct}%`}
          sub={
            <>
              {strings.dashboard.completionHint(figures.n)}
              {unmoderatedPct > 0 && <> · {strings.dashboard.unmoderatedHint(unmoderatedPct)}</>}
            </>
          }
        />
        <StatTile
          label={strings.dashboard.overdueLabel}
          value={strings.dashboard.stubDash}
          sub={strings.dashboard.stubPendingRegister}
        />
      </div>

      <section className="dashboard-card">
        <div className="dashboard-card-head">
          <h2 className="dashboard-card-title">{strings.dashboard.dimensionCardTitle}</h2>
          <p className="dashboard-card-hint">{strings.dashboard.dimensionCardHint}</p>
        </div>
        <table className="dashboard-table">
          <thead>
            <tr>
              <th scope="col">{strings.dashboard.dimensionHeader}</th>
              <th scope="col">{strings.dashboard.meanScoreHeader}</th>
              <th scope="col" className="dashboard-col-center">
                {strings.dashboard.levelHeader}
              </th>
              <th scope="col" className="dashboard-col-num">
                {strings.dashboard.initiativesHeader}
              </th>
            </tr>
          </thead>
          <tbody>
            {dimensions.map((d) => {
              const level = levelOfMean(d.mean);
              return (
                <tr key={d.key}>
                  <td>{d.name}</td>
                  <td>
                    <DimensionBar name={d.name} mean={d.mean} />
                  </td>
                  <td className="dashboard-col-center">
                    <span className={`maturity-badge maturity-badge-${level}`}>
                      {strings.assessment.levelAbbrev(level)}
                    </span>
                  </td>
                  <td className="dashboard-col-num dashboard-stub">{strings.dashboard.stubDash}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
        {weakest && (
          <p className="dashboard-callout">{strings.dashboard.maturityGapCallout(weakest.name, weakest.mean.toFixed(1))}</p>
        )}
      </section>

      <section className="dashboard-card">
        <h2 className="dashboard-card-title">{strings.dashboard.pipelineCardTitle}</h2>
        <div className="dashboard-pipeline">
          {[
            strings.dashboard.ideationLabel,
            strings.dashboard.developmentLabel,
            strings.dashboard.productionLabel,
            strings.dashboard.stoppedLabel,
          ].map((label) => (
            <div key={label} className="dashboard-pipeline-cell">
              <div className="dashboard-pipeline-value dashboard-stub">{strings.dashboard.stubDash}</div>
              <div className="dashboard-pipeline-label">{label}</div>
            </div>
          ))}
        </div>
        <p className="dashboard-card-hint">{strings.dashboard.pipelineStubBody}</p>
      </section>
    </>
  );
}
