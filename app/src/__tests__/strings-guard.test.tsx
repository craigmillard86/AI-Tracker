import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AppShell } from '../shell/AppShell';
import { strings } from '../strings';

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
  it('every visible shell text node comes from the strings module', () => {
    const { container } = render(<AppShell />);

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
