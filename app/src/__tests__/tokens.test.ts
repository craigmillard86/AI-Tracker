import { describe, expect, it } from 'vitest';
import tokensCss from '../design/tokens.css?raw';
import { expectedTokens } from '../design/tokens.expected';

/** Extract every `--name: value;` custom property from a CSS string. */
function parseCssVariables(css: string): Map<string, string> {
  const map = new Map<string, string>();
  const pattern = /(--[\w-]+)\s*:\s*([^;]+);/g;
  let match: RegExpExecArray | null;
  while ((match = pattern.exec(css)) !== null) {
    map.set(match[1], match[2].trim());
  }
  return map;
}

const tokens = parseCssVariables(tokensCss);

describe('design tokens match docs/design/DESIGN.md', () => {
  for (const [name, expected] of Object.entries(expectedTokens)) {
    it(`${name} = ${expected}`, () => {
      const actual = tokens.get(name);
      expect(actual, `tokens.css is missing ${name}`).toBeDefined();
      // Hex colours are case-insensitive; compare normalised to lowercase.
      expect(actual?.toLowerCase()).toBe(expected.toLowerCase());
    });
  }

  it('defines no unexpected extra custom properties', () => {
    const documented = new Set(Object.keys(expectedTokens));
    const extra = [...tokens.keys()].filter((name) => !documented.has(name));
    expect(extra).toEqual([]);
  });
});
