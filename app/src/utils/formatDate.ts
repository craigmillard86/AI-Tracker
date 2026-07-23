/**
 * Shared `en-GB` day/short-month/year date formatter (e.g. "23 Jul 2026"). Used across the register
 * screens/components — StageTimeline and RegisterDetailScreen both render dates from the same API
 * shapes and previously carried their own copy-pasted implementation (panel finding, HAP-14: one of
 * the two even claimed the other as a "shared" convention it wasn't). No locale story yet, so `en-GB`
 * is fixed here rather than threaded through as a parameter.
 */
export function formatDate(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }
  return date.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
}
