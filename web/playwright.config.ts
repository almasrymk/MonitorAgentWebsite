import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end and accessibility tests against the built apps (run `ng build site` first). The API is a small mock,
 * so these tests check the UI, not the backend (the backend has its own API tests).
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  reporter: process.env['CI'] ? 'github' : 'list',
  use: { baseURL: 'http://localhost:4400', trace: 'retain-on-failure' },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'] } },
    { name: 'mobile', use: { ...devices['Pixel 7'] } },
  ],
  webServer: [
    { command: 'node e2e/mock-api.mjs', port: 5099, reuseExistingServer: !process.env['CI'] },
    {
      command: 'node dist/site/server/server.mjs',
      port: 4400,
      reuseExistingServer: !process.env['CI'],
      env: { PORT: '4400', API_URL: 'http://localhost:5099', NG_ALLOWED_HOSTS: 'localhost' },
    },
  ],
});
