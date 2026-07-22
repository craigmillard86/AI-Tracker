import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AppShell } from '../shell/AppShell';
import { strings } from '../strings';

/** AppShell mounts AssessmentSelfScreen (HAP-8), which fetches on mount. A 404 (no open cycle)
 * keeps the rendered tree to shell chrome + strings-table literals — dynamic framework content
 * (dimension/level names, fetched only on the 200 path) is data, not a hard-coded literal, and is
 * intentionally out of this guard's scope. */
function installNoCycleFetchMock(): void {
  vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response('', { status: 404 }))));
}

/** Recursively collect every leaf string value from the strings table. */
function collectStrings(value: unknown): string[] {
  if (typeof value === 'string') {
    return [value];
  }
  if (value && typeof value === 'object') {
    return Object.values(value).flatMap(collectStrings);
  }
  return [];
}

describe('externalised strings (FR-067)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('every visible shell text node comes from the strings module', async () => {
    installNoCycleFetchMock();

    const { container } = render(<AppShell />);
    await screen.findByText(strings.assessment.noOpenCycleTitle);

    const known = new Set(
      collectStrings(strings)
        .map((s) => s.trim())
        .filter(Boolean),
    );

    const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
    const offenders: string[] = [];
    let node = walker.nextNode();
    while (node) {
      const text = node.textContent?.trim() ?? '';
      if (text && !known.has(text)) {
        offenders.push(text);
      }
      node = walker.nextNode();
    }

    expect(offenders, 'shell renders hard-coded literals not present in the strings module').toEqual(
      [],
    );
  });
});
