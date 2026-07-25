import { expect, test, type Page } from '@playwright/test'

const password = 'LocalOnly!2026'

async function signIn(page: Page, email: string) {
  await page.goto('/')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByText(email)).toBeVisible()
}

async function signOut(page: Page) {
  await page.getByRole('button', { name: 'Log out' }).click()
  await expect(page.getByRole('heading', { name: 'Sign in to ApprovalFlow' })).toBeVisible()
}

test('employee submission proceeds through manager and finance to final approval', async ({ page }) => {
  const vendor = `E2E Vendor ${Date.now()}`

  await signIn(page, 'employee.demo@local.test')
  await page.getByRole('button', { name: 'New request' }).click()
  await page.getByLabel('Vendor').fill(vendor)
  await page.getByLabel('Cost center').fill('E2E-300')
  await page.getByLabel('Category').selectOption('Software')
  await page.getByLabel('Requested delivery date').fill('2030-12-15')
  await page.getByLabel('Business justification').fill('Primary real SQL Server-backed Playwright workflow.')
  await page.getByLabel('Description').fill('Annual software subscription')
  await page.getByLabel('Quantity').fill('2')
  await page.getByLabel('Unit price').fill('750')
  await page.getByRole('button', { name: 'Create draft' }).click()

  await expect(page.getByRole('heading', { name: vendor })).toBeVisible()
  await expect(page.getByLabel(vendor).getByText('Draft', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Submit for approval' }).click()
  await expect(page.getByLabel(vendor).getByText('Pending Manager Approval', { exact: true })).toBeVisible()
  await signOut(page)

  await signIn(page, 'manager.demo@local.test')
  await page.getByRole('button', { name: 'Manager', exact: true }).click()
  await expect(page.getByText(vendor)).toBeVisible()
  await page.getByRole('row', { name: new RegExp(vendor) }).getByRole('button', { name: 'View' }).click()
  await page.getByLabel('Decision reason').fill('Manager confirms business need.')
  await page.getByRole('button', { name: 'Approve' }).click()
  await expect(page.getByLabel(vendor).getByText('Pending Finance Approval', { exact: true })).toBeVisible()
  await signOut(page)

  await signIn(page, 'finance.demo@local.test')
  await expect(page.getByText(vendor)).toBeVisible()
  await page.getByRole('row', { name: new RegExp(vendor) }).getByRole('button', { name: 'View' }).click()
  await page.getByLabel('Decision reason').fill('Finance confirms funding.')
  await page.getByRole('button', { name: 'Approve' }).click()
  await expect(page.getByLabel(vendor).getByText('Approved', { exact: true })).toBeVisible()

  const audit = page.getByRole('heading', { name: 'Audit history' }).locator('~ ol')
  await expect(audit.getByText('Draft → PendingManagerApproval')).toBeVisible()
  await expect(audit.getByText('PendingManagerApproval → PendingFinanceApproval')).toBeVisible()
  await expect(audit.getByText('PendingFinanceApproval → Approved')).toBeVisible()
  await expect(audit.locator('span').filter({ hasText: 'employee.demo@local.test' })).toBeVisible()
  await expect(audit.locator('span').filter({ hasText: 'manager.demo@local.test' })).toBeVisible()
  await expect(audit.locator('span').filter({ hasText: 'finance.demo@local.test' })).toBeVisible()
})
