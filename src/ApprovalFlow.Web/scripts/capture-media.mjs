import { mkdir } from 'node:fs/promises'
import { resolve } from 'node:path'
import { chromium } from '@playwright/test'

const mediaDir = resolve(process.cwd(), '../../docs/media')
await mkdir(mediaDir, { recursive: true })

const browser = process.env.PW_TEST_CONNECT_WS_ENDPOINT
  ? await chromium.connect(process.env.PW_TEST_CONNECT_WS_ENDPOINT)
  : await chromium.launch({ headless: true })
const context = await browser.newContext({
  viewport: { width: 1440, height: 900 },
})
const page = await context.newPage()
const password = 'LocalOnly!2026'
const vendor = `Phase 5 Demo ${Date.now()}`

async function signIn(email) {
  await page.goto('http://localhost:5173')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('banner').getByText(email).waitFor()
}

async function signOut() {
  await page.getByRole('button', { name: 'Log out' }).click()
}

async function reviewerPause() {
  await page.waitForTimeout(1_800)
}

await signIn('employee.demo@local.test')
await page.getByRole('button', { name: 'New request' }).click()
await page.getByLabel('Vendor').fill(vendor)
await page.getByLabel('Cost center').fill('DEMO-500')
await page.getByLabel('Category').selectOption('Software')
await page.getByLabel('Requested delivery date').fill('2030-12-15')
await page.getByLabel('Business justification').fill('Generated local demonstration request.')
await page.getByLabel('Description').fill('Team development subscription')
await page.getByLabel('Quantity').fill('2')
await page.getByLabel('Unit price').fill('750')
await page.getByRole('button', { name: 'Create draft' }).click()
await reviewerPause()
await page.getByRole('button', { name: 'Submit for approval' }).click()
await reviewerPause()
await page.screenshot({ path: resolve(mediaDir, 'employee-request.png'), fullPage: true })
await signOut()

await signIn('manager.demo@local.test')
await page.getByRole('button', { name: 'Manager', exact: true }).click()
await reviewerPause()
await page.getByRole('row', { name: new RegExp(vendor) }).getByRole('button', { name: 'View' }).click()
await reviewerPause()
await page.screenshot({ path: resolve(mediaDir, 'manager-review.png'), fullPage: true })
await page.getByLabel('Decision reason').fill('Manager confirms the generated business need.')
await page.getByRole('button', { name: 'Approve' }).click()
await reviewerPause()
await signOut()

await signIn('finance.demo@local.test')
await reviewerPause()
await page.getByRole('row', { name: new RegExp(vendor) }).getByRole('button', { name: 'View' }).click()
await reviewerPause()
await page.getByLabel('Decision reason').fill('Finance confirms the generated budget.')
await page.getByRole('button', { name: 'Approve' }).click()
await reviewerPause()
await page.screenshot({ path: resolve(mediaDir, 'finance-audit.png'), fullPage: true })

await context.close()

const observability = await browser.newPage({ viewport: { width: 1440, height: 900 } })
await observability.goto('http://localhost:18888')
await observability.waitForTimeout(3_000)
await observability.getByText('Traces', { exact: true }).click()
await observability.waitForTimeout(1_000)
await observability.screenshot({ path: resolve(mediaDir, 'local-observability.png'), fullPage: true })
await observability.close()
await browser.close()
