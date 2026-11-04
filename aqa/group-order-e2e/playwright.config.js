import { defineConfig } from '@playwright/test';

if (!process.env.UI_URL || !process.env.API_URL) throw new Error('Set UI_URL and API_URL to your own deployment.');
export default defineConfig({
  globalSetup: './auth-budget.js',
  testDir: './tests', timeout: 180_000, expect: { timeout: 20_000 },
  fullyParallel: false, workers: 1, retries: 0,
  reporter: [['list'], ['html', { open: 'never' }], ['junit', { outputFile: 'test-results/results.xml' }]],
  use: { baseURL: process.env.UI_URL, browserName: 'chromium', navigationTimeout: 60_000,
    // Network traces can contain bearer credentials; screenshots suffice for homework evidence.
    trace: 'off', screenshot: 'only-on-failure', video: 'off' },
});
