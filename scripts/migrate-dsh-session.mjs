/**
 * Move one DSH Web session from its current Workspace to another Workspace.
 *
 * This is an offline migration helper: the DeepSeek Harness desktop app must be
 * closed before it runs, because the session log is append-only while the app is
 * alive. The PowerShell wrapper performs backups and rollback.
 */
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { zstdCompressSync } from 'node:zlib';
import { decompress } from 'fzstd';

const args = parseArgs(process.argv.slice(2));
const dshHome = args['dsh-home'] || path.join(os.homedir(), '.dsh');
const sessionId = args['session-id'];
const targetPath = args['target-path'];
const sourceDir = args['source-dir'];
const targetDir = args['target-dir'];
const dryRun = args['dry-run'] === true || args['dry-run'] === 'true';

if (!sessionId || !targetPath || !sourceDir || !targetDir) {
  fail('missing --session-id, --target-path, --source-dir or --target-dir');
}

const workspaceFile = path.join(dshHome, 'storages', 'workspace.json');
const projectionFile = path.join(dshHome, 'storages', 'session_projcache', 'sessions', `${sessionId}.json`);
const sourceLog = path.join(sourceDir, 'session.v4.jsonl.zstd');
const targetLog = path.join(targetDir, 'session.v4.jsonl.zstd');

if (!fs.existsSync(workspaceFile)) fail(`workspace registry not found: ${workspaceFile}`);
if (!fs.existsSync(sourceLog)) fail(`session log not found: ${sourceLog}`);
if (fs.existsSync(targetLog)) fail(`target session already exists: ${targetLog}`);

const workspace = JSON.parse(fs.readFileSync(workspaceFile, 'utf8'));
const workspaces = workspace?.tables?.workspaces;
if (!workspaces || typeof workspaces !== 'object') fail('workspace.json has no tables.workspaces');

const normalizedTarget = normalizePath(targetPath);
const targetEntry = Object.entries(workspaces).find(([, value]) => normalizePath(value?.path) === normalizedTarget);
if (!targetEntry) fail(`target workspace not found for path: ${targetPath}`);
const [targetWorkspaceId, targetRecord] = targetEntry;

const sourceEntry = Object.entries(workspaces).find(([, value]) =>
  Array.isArray(value?.sessionIds) && value.sessionIds.includes(sessionId)
);
if (!sourceEntry) fail(`session '${sessionId}' is not accounted by any workspace`);
const [sourceWorkspaceId, sourceRecord] = sourceEntry;
if (sourceWorkspaceId === targetWorkspaceId) {
  console.log(JSON.stringify({ ok: true, alreadyInTarget: true, workspaceId: targetWorkspaceId }, null, 2));
  process.exit(0);
}

// Move the id out of every workspace before adding it to the target, preserving
// the single-owner invariant of the workspace registry.
for (const record of Object.values(workspaces)) {
  if (Array.isArray(record.sessionIds)) {
    record.sessionIds = record.sessionIds.filter((id) => id !== sessionId);
  }
}
targetRecord.sessionIds = [sessionId, ...(targetRecord.sessionIds || [])];
const now = new Date().toISOString();
targetRecord.updatedAt = now;
sourceRecord.updatedAt = now;

const projection = fs.existsSync(projectionFile)
  ? JSON.parse(fs.readFileSync(projectionFile, 'utf8'))
  : undefined;
if (projection?.record) {
  projection.record.identity = projection.record.identity || {};
  projection.record.identity.cwd = targetPath;
  if (projection.record.rows?.workingDirectory) {
    projection.record.rows.workingDirectory.val = targetPath;
  }
}

const logBytes = fs.readFileSync(sourceLog);
const plain = Buffer.from(decompress(logBytes));
const text = plain.toString('utf8');
const newline = text.indexOf('\n');
if (newline < 0) fail('session log header is missing');
const header = JSON.parse(text.slice(0, newline));
if (header.type !== 'session' || header.id !== sessionId) {
  fail('session log header does not match the requested session');
}
header.cwd = targetPath;
const rewritten = JSON.stringify(header) + text.slice(newline);
const compressed = zstdCompressSync(Buffer.from(rewritten, 'utf8'));

const summary = {
  ok: true,
  sessionId,
  fromWorkspaceId: sourceWorkspaceId,
  toWorkspaceId: targetWorkspaceId,
  fromPath: sourceRecord.path,
  toPath: targetRecord.path,
  sourceLogBytes: logBytes.length,
  rewrittenBytes: compressed.length,
  dryRun
};

if (dryRun) {
  console.log(JSON.stringify(summary, null, 2));
  process.exit(0);
}

// Prepare the target directory and stage every write before committing.
fs.mkdirSync(targetDir, { recursive: true });
const stagedLog = targetLog + '.staged';
const stagedWorkspace = workspaceFile + '.staged';
const stagedProjection = projectionFile + '.staged';
fs.writeFileSync(stagedLog, compressed);
fs.writeFileSync(stagedWorkspace, JSON.stringify(workspace, null, 2));
if (projection) fs.writeFileSync(stagedProjection, JSON.stringify(projection, null, 2));

try {
  fs.renameSync(stagedLog, targetLog);
  fs.renameSync(stagedWorkspace, workspaceFile);
  if (projection) fs.renameSync(stagedProjection, projectionFile);
  fs.rmSync(sourceDir, { recursive: true, force: true });
} catch (error) {
  for (const file of [stagedLog, stagedWorkspace, stagedProjection]) {
    try { fs.rmSync(file, { force: true }); } catch { /* best effort */ }
  }
  throw error;
}

console.log(JSON.stringify(summary, null, 2));

function parseArgs(argv) {
  const result = {};
  for (let index = 0; index < argv.length; index++) {
    const key = argv[index];
    if (!key.startsWith('--')) continue;
    const value = argv[index + 1];
    if (value === undefined || value.startsWith('--')) {
      result[key.slice(2)] = true;
    } else {
      result[key.slice(2)] = value;
      index++;
    }
  }
  return result;
}

function normalizePath(value) {
  return String(value || '').replace(/\//g, '\\').replace(/\\+$/, '').toLowerCase();
}

function fail(message) {
  console.error(JSON.stringify({ ok: false, error: message }, null, 2));
  process.exit(1);
}
