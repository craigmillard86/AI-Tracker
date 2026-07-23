import { useEffect, useMemo, useState } from 'react';
import {
  fetchBusinessUnits,
  fetchHarrisCategories,
  fetchInitiatives,
  type BusinessUnitResponse,
  type HarrisCategoryResponse,
  type InitiativeResponse,
  type InitiativeStage,
  type RagStatus,
} from '../../api/client';
import { LevelBadge } from '../../components/LevelBadge/LevelBadge';
import { RagChip } from '../../components/RagChip/RagChip';
import { StaleRowFlag } from '../../components/StaleRowFlag/StaleRowFlag';
import { strings } from '../../strings';

type RefState = 'loading' | 'error' | 'ready';

/** Enum-shaped internal state names (not framework/taxonomy content) — used as the option/query wire
 * values; their visible labels come from the strings table. */
const STAGE_ORDER: InitiativeStage[] = ['Idea', 'Evaluation', 'Pilot', 'Production', 'Scaled', 'Retired'];
const RAG_ORDER: RagStatus[] = ['OnTrack', 'AtRisk', 'OffTrack'];
const PAGE_SIZE = 25;

export interface RegisterListScreenProps {
  /** Wires the single primary CTA. HAP-14 owns the create form/detail — inert (no-op) here. */
  onNewInitiative?: () => void;
  /** Row activation target. HAP-14 owns the detail screen — a no-op here is fine. */
  onOpenInitiative?: (id: string) => void;
}

/** Whole days between an ISO timestamp and now (never negative in practice; clamped at 0 for display). */
function daysSince(iso: string): number {
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) {
    return 0;
  }
  return Math.max(0, Math.floor((Date.now() - then) / (1000 * 60 * 60 * 24)));
}

/**
 * The AI initiative register list (FR-026/027/034/035; layout/IA per docs/design/mockups/register-list.html,
 * binding). A filterable, paginated DataTable (DESIGN.md A4): sticky header, right-aligned numerics,
 * pagination above 25 rows. Search / BU / category / stage re-fetch server-side (FR-035 facets); the
 * mockup's RAG filter has no server param, so it is applied client-side over the fetched set. Every
 * row carries a level badge (number always printed), a RAG chip (label always printed) and, when stale,
 * a StaleRowFlag with the day count in its text (DESIGN.md A2/A8 colour-independence).
 */
export function RegisterListScreen({ onNewInitiative, onOpenInitiative }: RegisterListScreenProps = {}): JSX.Element {
  const [refState, setRefState] = useState<RefState>('loading');
  const [businessUnits, setBusinessUnits] = useState<BusinessUnitResponse[]>([]);
  const [categories, setCategories] = useState<HarrisCategoryResponse[]>([]);

  const [initiatives, setInitiatives] = useState<InitiativeResponse[]>([]);
  const [listError, setListError] = useState(false);

  const [searchTerm, setSearchTerm] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [buId, setBuId] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [stageFilter, setStageFilter] = useState('');
  const [ragFilter, setRagFilter] = useState('');
  const [page, setPage] = useState(1);

  // Reference data (BU + Harris category lists) — fetched once for the filter selects and cell lookups.
  useEffect(() => {
    let cancelled = false;
    Promise.all([fetchBusinessUnits(), fetchHarrisCategories()])
      .then(([units, cats]) => {
        if (cancelled) {
          return;
        }
        setBusinessUnits(units);
        setCategories(cats);
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
  }, []);

  // Debounce the search box ~250ms so it doesn't re-fetch on every keystroke; the other facets
  // (select-driven, not typed) re-fetch immediately.
  useEffect(() => {
    const handle = setTimeout(() => setDebouncedSearch(searchTerm), 250);
    return () => clearTimeout(handle);
  }, [searchTerm]);

  // Initiatives — re-fetched whenever a server-side facet changes (FR-035).
  useEffect(() => {
    let cancelled = false;
    setListError(false);
    fetchInitiatives({ search: debouncedSearch, bu: buId, category: categoryId, stage: stageFilter })
      .then((rows) => {
        if (!cancelled) {
          setInitiatives(rows);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setListError(true);
        }
      });
    return () => {
      cancelled = true;
    };
  }, [debouncedSearch, buId, categoryId, stageFilter]);

  // Any facet change (server- or client-side) returns to the first page.
  useEffect(() => {
    setPage(1);
  }, [debouncedSearch, buId, categoryId, stageFilter, ragFilter]);

  const buNameById = useMemo(() => new Map(businessUnits.map((b) => [b.id, b.name])), [businessUnits]);
  const categoryNameById = useMemo(() => new Map(categories.map((c) => [c.id, c.name])), [categories]);

  // RAG is filtered client-side (no server facet for it).
  const filtered = useMemo(
    () => (ragFilter ? initiatives.filter((i) => i.ragStatus === ragFilter) : initiatives),
    [initiatives, ragFilter],
  );
  const staleCount = useMemo(
    () => filtered.filter((i) => daysSince(i.lastUpdateAt) > 7).length,
    [filtered],
  );

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const currentPage = Math.min(page, totalPages);
  const pageRows = filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);
  const paginated = filtered.length > PAGE_SIZE;

  if (refState === 'loading') {
    return <p className="register-status">{strings.register.loading}</p>;
  }
  if (refState === 'error') {
    return (
      <p className="register-status register-status-error" role="alert">
        {strings.register.loadError}
      </p>
    );
  }

  function stageHarrisLabel(initiative: InitiativeResponse): string {
    const stage = strings.register.stageLabels[initiative.currentStage];
    if (!initiative.harrisStage) {
      return stage;
    }
    return strings.register.stageArrow(stage, strings.register.harrisStageLabels[initiative.harrisStage]);
  }

  return (
    <div className="register">
      <div className="register-head">
        <div>
          <div className="register-crumb">{strings.register.breadcrumb}</div>
          <h1 className="register-title">{strings.register.pageTitle}</h1>
          <p className="register-subtitle">{strings.register.subtitle}</p>
        </div>
        <button type="button" className="register-cta" onClick={() => onNewInitiative?.()}>
          {strings.register.newInitiative}
        </button>
      </div>

      <div className="register-filters" role="group" aria-label={strings.register.filtersLabel}>
        <label className="register-field register-field-grow">
          <span>{strings.register.searchLabel}</span>
          <input
            className="register-input"
            type="search"
            value={searchTerm}
            placeholder={strings.register.searchPlaceholder}
            onChange={(event) => setSearchTerm(event.target.value)}
          />
        </label>
        <label className="register-field">
          <span>{strings.register.buLabel}</span>
          <select className="register-input" value={buId} onChange={(event) => setBuId(event.target.value)}>
            <option value="">{strings.register.buAll}</option>
            {businessUnits.map((unit) => (
              <option key={unit.id} value={unit.id}>
                {unit.name}
              </option>
            ))}
          </select>
        </label>
        <label className="register-field">
          <span>{strings.register.categoryLabel}</span>
          <select
            className="register-input"
            value={categoryId}
            onChange={(event) => setCategoryId(event.target.value)}
          >
            <option value="">{strings.register.categoryAll}</option>
            {categories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.name}
              </option>
            ))}
          </select>
        </label>
        <label className="register-field">
          <span>{strings.register.stageLabel}</span>
          <select
            className="register-input"
            value={stageFilter}
            onChange={(event) => setStageFilter(event.target.value)}
          >
            <option value="">{strings.register.stageAll}</option>
            {STAGE_ORDER.map((stage) => (
              <option key={stage} value={stage}>
                {strings.register.stageLabels[stage]}
              </option>
            ))}
          </select>
        </label>
        <label className="register-field">
          <span>{strings.register.ragLabel}</span>
          <select
            className="register-input"
            value={ragFilter}
            onChange={(event) => setRagFilter(event.target.value)}
          >
            <option value="">{strings.register.ragAll}</option>
            {RAG_ORDER.map((rag) => (
              <option key={rag} value={rag}>
                {strings.register.ragLabels[rag]}
              </option>
            ))}
          </select>
        </label>
      </div>

      <section className="register-card">
        <div className="register-card-head">
          <div>
            <h2 className="register-card-title">{strings.register.cardTitle}</h2>
            <p className="register-card-hint">
              {strings.register.resultSummary(filtered.length)}
              {staleCount > 0 && (
                <>
                  {' · '}
                  <span className="register-stale-count">{strings.register.staleSummary(staleCount)}</span>
                </>
              )}
              {' · '}
              {strings.register.sortedNote}
            </p>
          </div>
          <div className="register-legend" aria-hidden="true">
            <LevelBadge level={1} />
            <LevelBadge level={2} />
            <LevelBadge level={3} />
            <span className="register-legend-label">{strings.register.legendLabel}</span>
          </div>
        </div>

        {listError ? (
          <p className="register-status register-status-error" role="alert">
            {strings.register.loadError}
          </p>
        ) : filtered.length === 0 ? (
          <div className="register-empty">
            <h3 className="register-empty-title">{strings.register.emptyTitle}</h3>
            <p className="register-status">{strings.register.emptyBody}</p>
          </div>
        ) : (
          <>
            <div className="register-table-wrap">
              <table className="register-table">
                <thead>
                  <tr>
                    <th scope="col">{strings.register.colInitiative}</th>
                    <th scope="col">{strings.register.colBu}</th>
                    <th scope="col">{strings.register.colCategory}</th>
                    <th scope="col">{strings.register.colStage}</th>
                    <th scope="col" className="register-col-center">
                      {strings.register.colLevel}
                    </th>
                    <th scope="col" className="register-col-center">
                      {strings.register.colRag}
                    </th>
                    <th scope="col" className="register-col-num">
                      {strings.register.colCustomers}
                    </th>
                    <th scope="col" className="register-col-num">
                      {strings.register.colLastUpdate}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {pageRows.map((initiative) => {
                    const days = daysSince(initiative.lastUpdateAt);
                    return (
                      <tr key={initiative.id}>
                        <td>
                          <button
                            type="button"
                            className="register-initiative-link"
                            onClick={() => onOpenInitiative?.(initiative.id)}
                          >
                            {initiative.name}
                          </button>
                        </td>
                        <td>{buNameById.get(initiative.businessUnitId) ?? strings.register.unknownBu}</td>
                        <td>
                          <span className="register-tag">
                            {categoryNameById.get(initiative.categoryId) ?? strings.register.unknownCategory}
                          </span>
                        </td>
                        <td>
                          <span className="register-stage">{stageHarrisLabel(initiative)}</span>
                        </td>
                        <td className="register-col-center">
                          <LevelBadge level={initiative.aiDlcLevel} />
                        </td>
                        <td className="register-col-center">
                          <RagChip status={initiative.ragStatus} />
                        </td>
                        <td className="register-col-num">
                          {initiative.customersInProduction ?? strings.register.emptyValue}
                        </td>
                        <td className="register-col-num">
                          <span className="register-lastupdate">
                            {strings.register.lastUpdateAgo(days)}
                            <StaleRowFlag days={days} />
                          </span>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            {paginated && (
              <div className="register-pagination">
                <button
                  type="button"
                  className="register-page-btn"
                  disabled={currentPage <= 1}
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                >
                  {strings.register.paginationPrev}
                </button>
                <span className="register-page-status" aria-live="polite">
                  {strings.register.paginationStatus(currentPage, totalPages)}
                </span>
                <button
                  type="button"
                  className="register-page-btn"
                  disabled={currentPage >= totalPages}
                  onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                >
                  {strings.register.paginationNext}
                </button>
              </div>
            )}
            <p className="register-card-hint register-pagination-hint">{strings.register.paginationHint}</p>
          </>
        )}
      </section>
    </div>
  );
}
