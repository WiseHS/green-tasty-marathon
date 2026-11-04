import { randomUUID } from 'node:crypto';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { writeFile, unlink } from 'node:fs/promises';

export default async function setup() {
  // Playwright may replace a worker after a failure or between files.
  // Keep pacing state outside workers; no credentials are stored here.
  const path = join(tmpdir(), `green-tasty-auth-${randomUUID()}.json`);
  process.env.AQA_AUTH_BUDGET_FILE = path;
  await writeFile(path, '[]');
  return async () => { await unlink(path).catch(error => { if (error.code !== 'ENOENT') throw error; }); };
}
