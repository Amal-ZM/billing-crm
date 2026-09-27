import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

/** Recursively lists every `.ts` file under `root` (plain Node `fs`, no Angular `TestBed` needed). */
function listTypeScriptSourceFiles(root: string): string[] {
  const files: string[] = [];

  for (const entry of readdirSync(root)) {
    const full = join(root, entry);
    if (statSync(full).isDirectory()) {
      files.push(...listTypeScriptSourceFiles(full));
    } else if (entry.endsWith('.ts')) {
      files.push(full);
    }
  }

  return files;
}

/**
 * Spec 012, User Story 3: nothing in the Angular app can ever turn user-supplied text into markup or a script.
 * Every screen that shows free text (customer name/address, product name, invoice notes, a payment note, invoice
 * history) already relies on Angular's own text interpolation, which escapes by construction; this test keeps that
 * true going forward by failing the day anyone adds an `innerHTML`/`outerHTML` binding or bypasses Angular's
 * sanitizer, rather than relying on a one-off manual review each spec.
 *
 * `*.spec.ts` files are excluded (this one included): a test legitimately reads a rendered element's `innerHTML`
 * to assert on its output (see invoice-print.component.spec.ts), which is not a vulnerability - only production
 * code setting it from untrusted input would be.
 */
describe('no source file uses innerHTML, outerHTML or bypasses the Angular sanitizer', () => {
  const offenders = ['innerHTML', 'outerHTML', 'bypassSecurityTrust'];

  it('finds no matches under src/app (excluding *.spec.ts)', () => {
    const files = listTypeScriptSourceFiles(__dirname).filter((file) => !file.endsWith('.spec.ts'));

    expect(files.length).toBeGreaterThan(0);

    const hits: string[] = [];
    for (const file of files) {
      const content = readFileSync(file, 'utf-8');
      for (const offender of offenders) {
        if (content.includes(offender)) {
          hits.push(`${file}: ${offender}`);
        }
      }
    }

    expect(hits).toEqual([]);
  });
});
