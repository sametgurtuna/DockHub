#!/usr/bin/env node
// Type-checks the sample widgets against sdk/types/dockhub.d.ts: the scripts of every sample (inline <script> blocks
// and .js files) are checked by TypeScript as JavaScript. Needs TypeScript: `npm install -g typescript`, then
//   node sdk/check-samples.mjs
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const samples = path.join(root, 'samples', 'widgets');
const types = path.join(root, 'sdk', 'types', 'dockhub.d.ts');
const work = fs.mkdtempSync(path.join(os.tmpdir(), 'dockhub-check-'));
const header = `/// <reference path="${types.replaceAll('\\', '/')}" />\n// @ts-check\n`;
const files = [];

for (const widget of fs.readdirSync(samples, { withFileTypes: true }).filter(d => d.isDirectory())) {
  const folder = path.join(samples, widget.name);
  for (const name of fs.readdirSync(folder)) {
    const source = fs.readFileSync(path.join(folder, name), 'utf8');
    const scripts = name.endsWith('.js') ? [source]
      : name.endsWith('.html') ? [...source.matchAll(/<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/gi)].map(m => m[1])
      : [];
    scripts.forEach((script, index) => {
      // Each file is a module of its own, so two samples' top-level names don't clash.
      const file = path.join(work, `${widget.name}-${path.basename(name)}-${index}.js`);
      fs.writeFileSync(file, header + script + '\nexport {};\n');
      files.push(file);
    });
  }
}

if (files.length === 0) {
  console.error('No sample scripts found.');
  process.exit(1);
}

const args = ['--noEmit', '--allowJs', '--checkJs', '--target', 'es2022', '--module', 'es2022', '--lib', 'es2022,dom,dom.iterable',
  '--skipLibCheck', ...files];
try {
  execFileSync(process.platform === 'win32' ? 'tsc.cmd' : 'tsc', args, { stdio: 'inherit', shell: process.platform === 'win32' });
  console.log(`${files.length} sample script(s) type-check against dockhub.d.ts.`);
} catch {
  console.error(`Scripts are in ${work} (each starts with two extra lines).`);
  process.exit(1);
}
fs.rmSync(work, { recursive: true, force: true });
