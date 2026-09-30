#!/usr/bin/env node
// dockhub-widget: create, check and package DockHub web widgets. Node 18 or newer, no dependencies.
//
//   node dockhub-widget.mjs create <id> [folder]   a new widget from a template
//   node dockhub-widget.mjs validate [folder]      the checks DockHub makes when it installs a widget
//   node dockhub-widget.mjs pack [folder] [-o file.dockwidget]
//
// The checks mirror DockHub's own (WebWidgetCatalog, WebWidgetDownloader); see docs/widget-sdk.md.
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import { fileURLToPath, pathToFileURL } from 'node:url';

export const ID_PATTERN = /^[a-z0-9][a-z0-9.\-]{2,63}$/;
export const MAX_PACKAGE_BYTES = 20 * 1024 * 1024;
export const MAX_UNPACKED_BYTES = 50 * 1024 * 1024;
export const MAX_PACKAGE_ENTRIES = 256;
export const MAX_LINK_FILE_BYTES = 5 * 1024 * 1024;
export const MAX_LINK_FILES = 64;
const SIZES = ['compact', 'standard', 'wide'];
const SETTING_TYPES = ['text', 'number', 'toggle', 'choice'];
/** Files a package leaves out: editor and tool files, and the source.json DockHub keeps in an installed widget. */
const NOT_PACKED = [/^\./, /^node_modules$/, /\.dockwidget$/i, /^dockhub\.d\.ts$/, /^jsconfig\.json$/, /^tsconfig\.json$/, /^source\.json$/];

/** manifest.json may have comments and trailing commas, as DockHub accepts them. */
export function parseJsonWithComments(text) {
  let out = '';
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (c === '"') {
      let j = i + 1;
      while (j < text.length && text[j] !== '"') j += text[j] === '\\' ? 2 : 1;
      out += text.slice(i, j + 1);
      i = j;
    } else if (c === '/' && text[i + 1] === '/') {
      while (i < text.length && text[i] !== '\n') i++;
      out += '\n';
    } else if (c === '/' && text[i + 1] === '*') {
      i = text.indexOf('*/', i + 2);
      if (i < 0) throw new SyntaxError('Unterminated comment');
      i++;
    } else {
      out += c;
    }
  }
  return JSON.parse(out.replace(/,(\s*[}\]])/g, '$1'));
}

/** "img/icon.png" is fine; absolute paths, "..", drive letters, backslashes and URLs are not (as in DockHub). */
export function isSafeRelativePath(p) {
  if (typeof p !== 'string' || p.trim().length === 0 || p.length > 200) return false;
  if (/[:\\?#]/.test(p) || p.startsWith('/')) return false;
  return p.split('/').every(s => s.length > 0 && s !== '.' && s !== '..');
}

/**
 * The manifest checks DockHub makes before it accepts a widget. Returns { errors, warnings }: errors make DockHub refuse
 * the widget, warnings point at things that probably don't work as meant.
 */
export function checkManifest(manifest) {
  const errors = [];
  const warnings = [];
  if (manifest === null || typeof manifest !== 'object' || Array.isArray(manifest)) return { errors: ['manifest.json is empty.'], warnings };
  if (typeof manifest.id !== 'string' || !ID_PATTERN.test(manifest.id))
    errors.push('The id must use lower-case letters, digits, dots and dashes (3 to 64 characters).');
  if (typeof manifest.name !== 'string' || manifest.name.trim().length === 0) errors.push('The name is missing.');
  const settings = Array.isArray(manifest.settings) ? manifest.settings : [];
  const permissions = manifest.permissions ?? {};
  for (const key of permissions.networkFromSettings ?? []) {
    const setting = settings.find(s => s.key === key && (s.type ?? 'text') === 'text');
    if (!setting) errors.push(`networkFromSettings names "${key}", which is not a text setting.`);
    else if (setting.default !== undefined && setting.default !== null) errors.push(`The server setting "${key}" can't have a default value.`);
  }

  if (manifest.version !== undefined && !/^\d+(\.\d+){0,3}([-+][0-9A-Za-z.\-+]*)?$/.test(String(manifest.version)))
    warnings.push(`version "${manifest.version}" isn't a version number like 1.2.0; updates compare versions.`);
  if (manifest.minDockHubVersion !== undefined && !/^\d+\.\d+(\.\d+){0,2}$/.test(String(manifest.minDockHubVersion)))
    warnings.push(`minDockHubVersion "${manifest.minDockHubVersion}" isn't a version like 0.9.0; DockHub ignores it.`);
  if (manifest.entry !== undefined && !isSafeRelativePath(manifest.entry)) errors.push(`The entry "${manifest.entry}" must be a file inside the widget folder.`);
  for (const variant of manifest.variants ?? []) {
    if (!SIZES.includes(variant.size ?? 'standard')) warnings.push(`Variant "${variant.id}": size must be compact, standard or wide.`);
  }
  for (const setting of settings) {
    if (typeof setting.key !== 'string' || setting.key.length === 0) warnings.push('A setting has no key.');
    if (!SETTING_TYPES.includes(setting.type ?? 'text')) warnings.push(`Setting "${setting.key}": type must be text, number, toggle or choice.`);
    if (setting.type === 'choice' && !(Array.isArray(setting.options) && setting.options.length > 0)) warnings.push(`Setting "${setting.key}": a choice needs options.`);
  }
  for (const host of permissions.network ?? []) {
    if (typeof host !== 'string' || /[/:]/.test(host)) warnings.push(`Network permission "${host}" must be a host name like api.example.com or *.example.com (no https://, no path).`);
  }
  for (const file of manifest.files ?? []) {
    if (!isSafeRelativePath(file)) errors.push(`The file "${file}" must be inside the widget folder.`);
  }
  if ((manifest.files ?? []).length + 1 > MAX_LINK_FILES) errors.push(`The widget lists too many files (at most ${MAX_LINK_FILES}).`);
  return { errors, warnings };
}

/** Files of a widget folder that a package would contain, relative with "/" separators. */
export function packedFiles(folder) {
  const files = [];
  const walk = relative => {
    for (const entry of fs.readdirSync(path.join(folder, relative), { withFileTypes: true })) {
      if (NOT_PACKED.some(rx => rx.test(entry.name))) continue;
      const rel = relative ? `${relative}/${entry.name}` : entry.name;
      if (entry.isDirectory()) walk(rel);
      else if (entry.isFile()) files.push(rel);
    }
  };
  walk('');
  return files.sort();
}

/** Every check DockHub makes on a widget folder: the manifest, the entry and listed files, and the package limits. */
export function validateFolder(folder) {
  const manifestPath = path.join(folder, 'manifest.json');
  if (!fs.existsSync(manifestPath)) return { manifest: null, errors: ['manifest.json is missing.'], warnings: [] };
  let manifest;
  try {
    manifest = parseJsonWithComments(fs.readFileSync(manifestPath, 'utf8'));
  } catch (error) {
    return { manifest: null, errors: [`manifest.json is invalid: ${error.message}`], warnings: [] };
  }
  const { errors, warnings } = checkManifest(manifest);
  const entry = manifest?.entry ?? 'index.html';
  if (isSafeRelativePath(entry) && !fs.existsSync(path.join(folder, entry))) errors.push(`The entry file ${entry} doesn't exist.`);
  for (const file of manifest?.files ?? []) {
    if (!isSafeRelativePath(file)) continue;
    const full = path.join(folder, file);
    if (!fs.existsSync(full)) errors.push(`The listed file ${file} doesn't exist.`);
    else if (fs.statSync(full).size > MAX_LINK_FILE_BYTES) errors.push(`${file} is larger than 5 MB (the limit when installing from a link).`);
  }
  const files = packedFiles(folder);
  if (files.length > MAX_PACKAGE_ENTRIES) errors.push(`The package would have more than ${MAX_PACKAGE_ENTRIES} files.`);
  const unpacked = files.reduce((sum, f) => sum + fs.statSync(path.join(folder, f)).size, 0);
  if (unpacked > MAX_UNPACKED_BYTES) errors.push('The widget is larger than 50 MB.');
  // Installing from a link downloads only the entry and "files": everything else would be missing.
  const listed = new Set([entry, 'manifest.json', ...(manifest?.files ?? [])]);
  const unlisted = files.filter(f => !listed.has(f));
  if (unlisted.length > 0) warnings.push(`Not in "files", so missing when installed from a link: ${unlisted.join(', ')}`);
  return { manifest, errors, warnings };
}

// ------------------------------------------------------------------ zip

const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c >>> 0;
  }
  return table;
})();

export function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) crc = CRC_TABLE[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

/** A zip (deflated entries, UTF-8 names) of the given { name, data } entries. */
export function zip(entries, date = new Date()) {
  const dosTime = (date.getHours() << 11) | (date.getMinutes() << 5) | Math.floor(date.getSeconds() / 2);
  const dosDate = ((date.getFullYear() - 1980) << 9) | ((date.getMonth() + 1) << 5) | date.getDate();
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const { name, data } of entries) {
    const nameBytes = Buffer.from(name, 'utf8');
    const compressed = zlib.deflateRawSync(data, { level: 9 });
    const crc = crc32(data);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(0x0800, 6); // UTF-8 names
    local.writeUInt16LE(8, 8); // deflate
    local.writeUInt16LE(dosTime, 10);
    local.writeUInt16LE(dosDate, 12);
    local.writeUInt32LE(crc, 14);
    local.writeUInt32LE(compressed.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(nameBytes.length, 26);
    local.writeUInt16LE(0, 28);
    locals.push(local, nameBytes, compressed);

    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt16LE(0x0800, 8);
    central.writeUInt16LE(8, 10);
    central.writeUInt16LE(dosTime, 12);
    central.writeUInt16LE(dosDate, 14);
    central.writeUInt32LE(crc, 16);
    central.writeUInt32LE(compressed.length, 20);
    central.writeUInt32LE(data.length, 24);
    central.writeUInt16LE(nameBytes.length, 28);
    central.writeUInt32LE(offset, 42);
    centrals.push(central, nameBytes);
    offset += local.length + nameBytes.length + compressed.length;
  }
  const centralSize = centrals.reduce((sum, b) => sum + b.length, 0);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(entries.length, 8);
  end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(centralSize, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, ...centrals, end]);
}

/** Packs a widget folder into a .dockwidget; throws with the reasons when DockHub would refuse it. */
export function pack(folder, output) {
  const result = validateFolder(folder);
  if (result.errors.length > 0) throw new Error(result.errors.join('\n'));
  const files = packedFiles(folder);
  const data = zip(files.map(name => ({ name, data: fs.readFileSync(path.join(folder, name)) })));
  if (data.length > MAX_PACKAGE_BYTES) throw new Error('The package is larger than 20 MB.');
  const target = output ?? path.join(process.cwd(), `${result.manifest.id}-${result.manifest.version ?? '1.0.0'}.dockwidget`);
  fs.writeFileSync(target, data);
  return { file: target, files, warnings: result.warnings };
}

// ------------------------------------------------------------------ create

const TYPES = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'sdk', 'types', 'dockhub.d.ts');

export function create(id, folder) {
  if (!ID_PATTERN.test(id)) throw new Error('The id must use lower-case letters, digits, dots and dashes (3 to 64 characters), for example com.example.my-widget.');
  if (fs.existsSync(folder) && fs.readdirSync(folder).length > 0) throw new Error(`${folder} isn't empty.`);
  fs.mkdirSync(folder, { recursive: true });
  const name = id.split('.').pop().replace(/[-_]/g, ' ').replace(/^\w/, c => c.toUpperCase());
  const manifest = {
    id,
    name,
    version: '1.0.0',
    author: '',
    description: 'What the widget shows, in one or two sentences.',
    entry: 'index.html',
    minDockHubVersion: '0.9.0',
    variants: [
      { id: 'standard', name: 'Standard', size: 'standard' },
      { id: 'mini', name: 'Mini', size: 'compact' },
    ],
    settings: [{ key: 'label', type: 'text', label: 'Label', default: name }],
    permissions: { network: [], notifications: false },
    files: ['widget.js'],
  };
  fs.writeFileSync(path.join(folder, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
  fs.writeFileSync(path.join(folder, 'index.html'), `<!doctype html>
<html>
<head>
<meta charset="utf-8">
<style>
  html, body { margin: 0; height: 100%; background: transparent; overflow: hidden; }
  body {
    font-family: var(--dh-font, "Segoe UI", sans-serif); color: var(--dh-text, #fff);
    display: flex; align-items: center; justify-content: center; user-select: none;
  }
  #label { font-size: 13px; font-weight: 600; }
  html[data-size="compact"] #label { font-size: 11px; }
</style>
</head>
<body>
  <div id="label"></div>
  <script src="widget.js"></script>
</body>
</html>
`);
  fs.writeFileSync(path.join(folder, 'widget.js'), `// @ts-check
/// <reference path="dockhub.d.ts" />

async function start() {
  const label = /** @type {HTMLElement} */ (document.getElementById('label'));
  const show = (/** @type {DockHub.Settings} */ settings) => { label.textContent = settings.label; };
  show(await dockhub.settings.get());
  dockhub.settings.onChange(show);
  dockhub.onSize(size => { document.documentElement.dataset.size = size; });
}

start();
`);
  if (fs.existsSync(TYPES)) fs.copyFileSync(TYPES, path.join(folder, 'dockhub.d.ts'));
  fs.writeFileSync(path.join(folder, 'jsconfig.json'), JSON.stringify({ compilerOptions: { checkJs: true, target: 'es2022', lib: ['es2022', 'dom'] } }, null, 2) + '\n');
  return folder;
}

// ------------------------------------------------------------------ command line

function main(args) {
  const [command, ...rest] = args;
  const print = (lines, prefix) => lines.forEach(line => console.log(`${prefix} ${line}`));
  switch (command) {
    case 'create': {
      const [id, folder] = rest;
      if (!id) throw new Error('Usage: dockhub-widget create <id> [folder]');
      const created = create(id, path.resolve(folder ?? id.split('.').pop()));
      console.log(`Created ${created}. Copy it to %AppData%\\DockHub\\widgets\\${id} to try it (see docs/widget-sdk.md).`);
      return 0;
    }
    case 'validate': {
      const folder = path.resolve(rest[0] ?? '.');
      const { errors, warnings } = validateFolder(folder);
      print(errors, 'error:');
      print(warnings, 'warning:');
      console.log(errors.length === 0 ? `${folder}: DockHub can install this widget.` : `${folder}: DockHub would refuse this widget.`);
      return errors.length === 0 ? 0 : 1;
    }
    case 'pack': {
      const outIndex = rest.indexOf('-o');
      const output = outIndex >= 0 ? path.resolve(rest[outIndex + 1]) : undefined;
      const folder = path.resolve(rest.find((a, i) => i !== outIndex && i !== outIndex + 1) ?? '.');
      const result = pack(folder, output);
      print(result.warnings, 'warning:');
      console.log(`Packed ${result.files.length} file(s) into ${result.file}.`);
      return 0;
    }
    default:
      console.log('Usage:\n  dockhub-widget create <id> [folder]\n  dockhub-widget validate [folder]\n  dockhub-widget pack [folder] [-o file.dockwidget]');
      return command ? 1 : 0;
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(fs.realpathSync(process.argv[1])).href) {
  try {
    process.exitCode = main(process.argv.slice(2));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
