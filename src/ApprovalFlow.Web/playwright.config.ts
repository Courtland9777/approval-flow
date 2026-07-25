import { defineConfig, devices } from '@playwright/test'

const databaseName = process.env.APPROVALFLOW_E2E_DATABASE
const connectionString = process.env.APPROVALFLOW_E2E_CONNECTION

if (!databaseName || !connectionString) {
  throw new Error('Run Playwright through npm run e2e so the dedicated database lifecycle is enforced.')
}

export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  outputDir: 'test-results',
  use: {
    baseURL: 'http://127.0.0.1:4173',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: [
    {
      command: 'dotnet run --no-build --project ../ApprovalFlow.Api --urls http://127.0.0.1:5081',
      url: 'http://127.0.0.1:5081/openapi/v1.json',
      reuseExistingServer: false,
      timeout: 120_000,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        SeedDevelopmentData: 'true',
        ConnectionStrings__ApprovalFlow: connectionString,
      },
    },
    {
      command: 'vite --host 0.0.0.0 --port 4173',
      url: 'http://127.0.0.1:4173',
      reuseExistingServer: false,
      timeout: 120_000,
      env: {
        APPROVALFLOW_PROXY_TARGET: 'http://127.0.0.1:5081',
      },
    },
  ],
})
