// Updates catalog/catalog.json from the GitHub releases of each module's repository.
//
// A module repository publishes a release with a `<id>-<x.y.z>.zip` asset (docs/MODULES.md). For every catalog
// module this script finds the newest such asset in its source repository (the GitHub `homepage`, or this
// repository for modules without one), and when it is newer than the catalog version it downloads it, checks
// that its module.json carries the same id and version, and writes version, package, sha256, size and the
// release notes into the catalog. A re-uploaded asset under the same URL only refreshes sha256 and size.
//
// Release notes come from the `releaseNotes` entry for that version in the package's module.json
// ([{ "version", "notes": [English sentences] }]), falling back to the bullet lines of the GitHub release body.
//
//   node scripts/sync-catalog.mjs [--dry-run]       GITHUB_TOKEN or GH_TOKEN raises the API rate limit
//
// Prints one "<id> <old> -> <new>" line per change; the workflow uses them as the commit message.

import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { inflateRawSync } from 'node:zlib';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const catalogPath = join(root, 'catalog', 'catalog.json');
const ownRepo = process.env.GITHUB_REPOSITORY || 'luingry/SwissTechKnife';
const token = process.env.GITHUB_TOKEN || process.env.GH_TOKEN;
const dryRun = process.argv.includes('--dry-run');

const headers = { 'User-Agent': 'stk-catalog-sync', Accept: 'application/vnd.github+json' };
if (token) headers.Authorization = `Bearer ${token}`;

async function api(path) {
  const response = await fetch(`https://api.github.com/${path}`, { headers });
  if (!response.ok) throw new Error(`GET ${path}: HTTP ${response.status}`);
  return response.json();
}

async function download(url) {
  const response = await fetch(url, { headers: { 'User-Agent': headers['User-Agent'] }, redirect: 'follow' });
  if (!response.ok) throw new Error(`GET ${url}: HTTP ${response.status}`);
  return Buffer.from(await response.arrayBuffer());
}

function compareVersions(a, b) {
  const pa = a.split('.').map(Number);
  const pb = b.split('.').map(Number);
  for (let i = 0; i < 3; i++) {
    if ((pa[i] || 0) !== (pb[i] || 0)) return (pa[i] || 0) - (pb[i] || 0);
  }
  return 0;
}

function sourceRepo(entry) {
  const match = /^https:\/\/github\.com\/([^/]+\/[^/#?]+)/i.exec(entry.homepage || '');
  return match ? match[1].replace(/\.git$/i, '') : ownRepo;
}

// Reads one root-level file from a zip (stored or deflated), without extracting anything to disk.
function readZipEntry(zip, name) {
  let end = -1;
  for (let i = zip.length - 22; i >= Math.max(0, zip.length - 22 - 0xffff); i--) {
    if (zip.readUInt32LE(i) === 0x06054b50) { end = i; break; }
  }
  if (end < 0) throw new Error('not a zip file');
  const count = zip.readUInt16LE(end + 10);
  let offset = zip.readUInt32LE(end + 16);
  for (let n = 0; n < count; n++) {
    if (zip.readUInt32LE(offset) !== 0x02014b50) throw new Error('corrupt zip directory');
    const method = zip.readUInt16LE(offset + 10);
    const compressedSize = zip.readUInt32LE(offset + 20);
    const nameLength = zip.readUInt16LE(offset + 28);
    const extraLength = zip.readUInt16LE(offset + 30);
    const commentLength = zip.readUInt16LE(offset + 32);
    const local = zip.readUInt32LE(offset + 42);
    const entryName = zip.toString('utf8', offset + 46, offset + 46 + nameLength);
    if (entryName === name) {
      const start = local + 30 + zip.readUInt16LE(local + 26) + zip.readUInt16LE(local + 28);
      const data = zip.subarray(start, start + compressedSize);
      if (method === 0) return data;
      if (method === 8) return inflateRawSync(data);
      throw new Error(`unsupported zip compression ${method}`);
    }
    offset += 46 + nameLength + extraLength + commentLength;
  }
  return null;
}

function notesFromBody(body) {
  return (body || '')
    .split(/\r?\n/)
    .map((line) => /^\s*[-*]\s+(.+)$/.exec(line))
    .filter(Boolean)
    .map((match) => match[1].replace(/\*\*|`/g, '').trim())
    .filter(Boolean)
    .slice(0, 10);
}

async function newestRelease(entry, repo) {
  const pattern = new RegExp(`^${entry.id.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}-(\\d+\\.\\d+\\.\\d+)\\.zip$`);
  let best = null;
  for (const release of await api(`repos/${repo}/releases?per_page=50`)) {
    if (release.draft || release.prerelease) continue;
    for (const asset of release.assets) {
      const match = pattern.exec(asset.name);
      if (match && (!best || compareVersions(match[1], best.version) > 0)) {
        best = { version: match[1], asset, release };
      }
    }
  }
  return best;
}

async function sync(entry) {
  const repo = sourceRepo(entry);
  const found = await newestRelease(entry, repo);
  if (!found) return null;
  const { version, asset, release } = found;
  const url = asset.browser_download_url;
  const newer = !entry.version || compareVersions(version, entry.version) > 0;
  const sameUrl = entry.package === url;
  const digest = /^sha256:([0-9a-f]{64})$/.exec(asset.digest || '')?.[1];
  if (!newer && !(sameUrl && digest && digest !== entry.sha256)) return null;

  const zip = await download(url);
  const sha256 = createHash('sha256').update(zip).digest('hex');
  if (digest && digest !== sha256) throw new Error(`${entry.id}: ${asset.name} does not match its GitHub digest`);
  const manifestBytes = readZipEntry(zip, 'module.json');
  if (!manifestBytes) throw new Error(`${entry.id}: ${asset.name} has no module.json at its root`);
  const manifest = JSON.parse(manifestBytes.toString('utf8').replace(/^﻿/, ''));
  if (manifest.id !== entry.id || manifest.version !== version) {
    throw new Error(`${entry.id}: ${asset.name} contains module.json for ${manifest.id} ${manifest.version}`);
  }
  if (!newer && sha256 === entry.sha256) return null;

  const previous = entry.version;
  entry.version = version;
  entry.package = url;
  entry.sha256 = sha256;
  entry.size = zip.length;
  if (newer) {
    const own = (Array.isArray(manifest.releaseNotes) ? manifest.releaseNotes : [])
      .find((note) => note && note.version === version && Array.isArray(note.notes) && note.notes.length);
    const notes = own ? own.notes.map(String) : notesFromBody(release.body);
    const date = new Date(release.published_at || release.created_at || Date.now())
      .toLocaleDateString('en-CA', { timeZone: 'America/Sao_Paulo' });
    entry.releaseNotes = [
      { version, date, notes: notes.length ? notes : [`Version ${version}.`] },
      ...(entry.releaseNotes || []).filter((note) => note.version !== version),
    ];
  }
  return newer ? `${entry.id} ${previous} -> ${version}` : `${entry.id} ${version} (package re-uploaded)`;
}

const text = readFileSync(catalogPath, 'utf8');
const catalog = JSON.parse(text);
const changes = [];
let failed = false;
for (const entry of catalog.modules) {
  try {
    const change = await sync(entry);
    if (change) changes.push(change);
  } catch (error) {
    // One broken module repository never blocks the others.
    failed = true;
    console.error(`::warning::${entry.id}: ${error.message}`);
  }
}

if (changes.length && !dryRun) writeFileSync(catalogPath, JSON.stringify(catalog, null, 2) + '\n');
for (const change of changes) console.log(change);
if (failed && !changes.length) process.exitCode = 1;
