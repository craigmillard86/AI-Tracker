import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * QA negative-path guard (HAP-1): the constitution forbids any runtime network
 * dependency, so fonts must be self-hosted (@fontsource) and never fetched from an
 * external host. verify.sh already greps the *built* dist for `fonts.googleapis`;
 * this asserts the same at *source* level across every file under src/, catching an
 * external-font reference before it ever reaches a build.
 */
const SRC_ROOT = join(process.cwd(), 'src');
const FORBIDDEN = [/fonts\.googleapis/i, /fonts\.gstatic/i, /@import\s+url\(\s*['"]?https?:/i];

function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      // Tests never ship in the production bundle; scope the guard to app source.
      return entry === '__tests__' ? [] : sourceFiles(full);
    }
    return /\.(tsx?|css)$/.test(entry) ? [full] : [];
  });
}

describe('no external font requests in source (constitution Art. X)', () => {
  it('no src file references an external font host', () => {
    const offenders = sourceFiles(SRC_ROOT).filter((file) => {
      const contents = readFileSync(file, 'utf8');
      return FORBIDDEN.some((pattern) => pattern.test(contents));
    });
    expect(offenders, 'external font/host reference found in source').toEqual([]);
  });
});
