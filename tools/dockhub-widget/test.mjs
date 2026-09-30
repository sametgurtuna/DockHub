// Tests of dockhub-widget: node --test tools/dockhub-widget/test.mjs
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import zlib from 'node:zlib';
import { fileURLToPath } from 'node:url';
import {
  checkManifest, create, crc32, isSafeRelativePath, pack, parseJsonWithComments, validateFolder, zip,
} from './dockhub-widget.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const temp = () => fs.mkdtempSync(path.join(os.tmpdir(), 'dockhub-widget-test-'));

/** The entries of a zip, read back from its central directory. */
function unzip(buffer) {
  const end = buffer.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06]));
  const count = buffer.readUInt16LE(end + 10);
  let offset = buffer.readUInt32LE(end + 16);
  const entries = {};
  for (let i = 0; i < count; i++) {
    const method = buffer.readUInt16LE(offset + 10);
    const crc = buffer.readUInt32LE(offset + 16);
    const size = buffer.readUInt32LE(offset + 20);
    const nameLength = buffer.readUInt16LE(offset + 28);
    const local = buffer.readUInt32LE(offset + 42);
    const name = buffer.subarray(offset + 46, offset + 46 + nameLength).toString('utf8');
    const localName = buffer.readUInt16LE(local + 26);
    const localExtra = buffer.readUInt16LE(local + 28);
    const data = buffer.subarray(local + 30 + localName + localExtra, local + 30 + localName + localExtra + size);
    const content = method === 8 ? zlib.inflateRawSync(data) : data;
    assert.equal(crc32(content), crc, `crc of ${name}`);
    entries[name] = content.toString('utf8');
    offset += 46 + nameLength + buffer.readUInt16LE(offset + 30) + buffer.readUInt16LE(offset + 32);
  }
  return entries;
}

test('manifests are judged as DockHub judges them', () => {
  const { cases } = JSON.parse(fs.readFileSync(path.join(root, 'tests', 'fixtures', 'widget-manifest-cases.json'), 'utf8'));
  for (const item of cases) {
    const { errors } = checkManifest(item.manifest);
    assert.equal(errors.length === 0, item.valid, `${item.name}: ${errors.join(' ')}`);
  }
});

test('every sample widget can be installed', () => {
  const samples = path.join(root, 'samples', 'widgets');
  for (const name of fs.readdirSync(samples).filter(n => fs.statSync(path.join(samples, n)).isDirectory())) {
    const { errors } = validateFolder(path.join(samples, name));
    assert.deepEqual(errors, [], name);
  }
});

test('comments and trailing commas are read like DockHub reads them', () => {
  const manifest = parseJsonWithComments('{\n // a comment\n "id": "dev.x.y", /* another */ "name": "a // not a comment",\n}');
  assert.deepEqual(manifest, { id: 'dev.x.y', name: 'a // not a comment' });
});

test('paths must stay in the widget folder', () => {
  for (const good of ['index.html', 'img/icon.png', 'a/b/c.js']) assert.ok(isSafeRelativePath(good), good);
  for (const bad of ['', '/abs.js', '../up.js', 'a/../b.js', 'C:/x.js', 'a\\b.js', 'https://x/y.js', 'a//b.js', 'x.js?v=1', './a.js'])
    assert.ok(!isSafeRelativePath(bad), bad);
});

test('a created widget validates, type-checks its API use and packs', () => {
  const dir = temp();
  const folder = create('com.example.test-widget', path.join(dir, 'test-widget'));
  const result = validateFolder(folder);
  assert.deepEqual(result.errors, []);
  assert.deepEqual(result.warnings, []);

  const output = path.join(dir, 'out.dockwidget');
  const packed = pack(folder, output);
  // Editor files stay out of the package.
  assert.deepEqual(packed.files, ['index.html', 'manifest.json', 'widget.js']);
  const entries = unzip(fs.readFileSync(output));
  assert.deepEqual(Object.keys(entries).sort(), ['index.html', 'manifest.json', 'widget.js']);
  assert.equal(JSON.parse(entries['manifest.json']).id, 'com.example.test-widget');
});

test('a widget DockHub would refuse is not packed', () => {
  const dir = temp();
  fs.writeFileSync(path.join(dir, 'manifest.json'), JSON.stringify({ id: 'Bad Id', name: 'X' }));
  fs.writeFileSync(path.join(dir, 'index.html'), '<p>x</p>');
  assert.throws(() => pack(dir, path.join(dir, 'x.dockwidget')), /id must use/);

  const noEntry = temp();
  fs.writeFileSync(path.join(noEntry, 'manifest.json'), JSON.stringify({ id: 'dev.test.x', name: 'X', entry: 'main.html' }));
  assert.match(validateFolder(noEntry).errors.join(), /main\.html doesn't exist/);
});

test('files missing from "files" are pointed out', () => {
  const dir = temp();
  fs.writeFileSync(path.join(dir, 'manifest.json'), JSON.stringify({ id: 'dev.test.files', name: 'X' }));
  fs.writeFileSync(path.join(dir, 'index.html'), '<script src="app.js"></script>');
  fs.writeFileSync(path.join(dir, 'app.js'), '');
  assert.match(validateFolder(dir).warnings.join(), /app\.js/);
});

test('the zip format is standard', () => {
  const data = zip([{ name: 'ä/ü.txt', data: Buffer.from('hello hello hello') }, { name: 'empty.txt', data: Buffer.alloc(0) }]);
  assert.deepEqual(unzip(data), { 'ä/ü.txt': 'hello hello hello', 'empty.txt': '' });
  assert.equal(crc32(Buffer.from('123456789')), 0xcbf43926);
});
