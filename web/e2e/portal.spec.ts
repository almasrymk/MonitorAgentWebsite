import AxeBuilder from '@axe-core/playwright';
import { expect, Page, test } from '@playwright/test';
import { FakePortalApi } from './fake-portal-api';

const portal = 'http://localhost:4401/portal';
const wcag = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

async function open(page: Page, lang: 'ar' | 'en' = 'en'): Promise<FakePortalApi> {
  const api = new FakePortalApi();
  await api.install(page);
  await page.addInitScript((l) => localStorage.setItem('ma.lang', l), lang);
  return api;
}

async function expectAccessible(page: Page): Promise<void> {
  const results = await new AxeBuilder({ page }).withTags(wcag).analyze();
  expect(results.violations.map((v) => `${v.id}: ${v.help} at ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`)).toEqual([]);
}

async function signIn(page: Page): Promise<void> {
  await page.getByLabel('Email').fill('owner@example.test');
  await page.getByLabel('Password', { exact: true }).fill('correct horse battery');
  await page.getByRole('button', { name: 'Sign in' }).click();
}

test('a signed-out visitor signs in and comes back to the page they asked for', async ({ page }) => {
  await open(page);
  await page.goto(`${portal}/licenses`);
  await expect(page).toHaveURL(/\/portal\/sign-in\?returnUrl=%2Flicenses/);
  await expectAccessible(page);

  await page.getByLabel('Email').fill('owner@example.test');
  await page.getByLabel('Password', { exact: true }).fill('wrong password');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('alert')).toBeVisible();

  await signIn(page);
  await expect(page).toHaveURL(`${portal}/licenses`);
  await expect(page.getByRole('link', { name: 'LIC-2026-0001' })).toBeVisible();
});

test('sign-in asks for the authenticator code when MFA is on', async ({ page }) => {
  const api = await open(page);
  api.requiresMfa = true;
  await page.goto(`${portal}/sign-in`);
  await signIn(page);
  await page.getByLabel('Code', { exact: true }).fill('123456');
  await page.getByRole('button', { name: /verify/i }).click();
  await expect(page).toHaveURL(`${portal}/`);
  expect(api.calls).toContain('POST /auth/login/mfa');
});

test('a sign-in link cannot send the user to another site', async ({ page }) => {
  await open(page);
  await page.goto(`${portal}/sign-in?returnUrl=//evil.example`);
  await signIn(page);
  await expect(page).toHaveURL(`${portal}/`);
});

test('an owner buys a license, pays and gets the invoice', async ({ page }) => {
  const api = await open(page);
  api.signedIn = true;
  await page.goto(`${portal}/checkout?plan=11111111-1111-1111-1111-111111111111`);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await expectAccessible(page);
  await page.getByRole('button', { name: 'Continue to payment' }).click();

  await expect(page).toHaveURL(`${portal}/orders/o1`);
  await expect(page.getByText('LIC-2026-0002')).toBeVisible();
  await page.goto(`${portal}/invoices`);
  await expect(page.getByText('INV-2026-000001')).toBeVisible();
});

const signedInPages = ['', '/licenses', '/licenses/l1', '/orders', '/invoices', '/support', '/support/new', '/support/t1', '/suggestions', '/company', '/account'];
for (const lang of ['ar', 'en'] as const) {
  for (const path of signedInPages) {
    test(`${lang} ${path || '/'} has no accessibility violations`, async ({ page }) => {
      const api = await open(page, lang);
      api.signedIn = true;
      await page.goto(`${portal}${path}`);
      await expect(page.locator('html')).toHaveAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      await expect(page.locator('.skeleton')).toHaveCount(0);
      await expectAccessible(page);
      expect(await page.evaluate(() => window.innerWidth), 'page is wider than the screen').toBe(page.viewportSize()!.width);
    });
  }
}

test('the owner invites a teammate', async ({ page }) => {
  const api = await open(page);
  api.signedIn = true;
  await page.goto(`${portal}/company`);
  await page.locator('#invite-email').fill('new@example.test');
  await page.getByRole('button', { name: /invitation/i }).click();
  await expect(page.getByRole('status')).toContainText('new@example.test');
  await expect(page.getByRole('cell', { name: 'new@example.test' })).toBeVisible();
});

test('the product key is shown only when asked for', async ({ page }) => {
  const api = await open(page);
  api.signedIn = true;
  await page.goto(`${portal}/licenses/l1`);
  await expect(page.getByText('UBG-7K2Q-AAAA-BBBB-CCCC')).toHaveCount(0);
  await page.getByRole('button', { name: 'Show product key' }).click();
  await expect(page.getByText('UBG-7K2Q-AAAA-BBBB-CCCC')).toBeVisible();
});

test('each member shows their own role, and the page fits a phone screen', async ({ page }) => {
  const api = await open(page);
  api.signedIn = true;
  await page.goto(`${portal}/company`);
  await expect(page.getByLabel('Change role for Tarek Tech')).toHaveValue('Technical');
  // Phones widen the layout to fit anything that overflows, so the layout must stay as wide as the device.
  expect(await page.evaluate(() => window.innerWidth)).toBe(page.viewportSize()!.width);
});

test('a customer opens a ticket and follows the conversation', async ({ page }) => {
  const api = await open(page);
  api.signedIn = true;
  await page.goto(`${portal}/support`);
  await page.getByRole('link', { name: 'New ticket' }).click();
  await page.getByLabel('Subject').fill('Cannot activate <b>PC-02</b>');
  await page.getByLabel('Topic').selectOption('Licensing');
  await page.getByLabel('What happened?').fill('It says the license is full.');
  await page.getByRole('button', { name: 'Send ticket' }).click();
  await expect(page).toHaveURL(`${portal}/support/t2`);
  // Shown as text, never as markup.
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Cannot activate <b>PC-02</b>');

  await page.getByLabel('Your reply').fill('Also PC-03.');
  await page.getByRole('button', { name: 'Send', exact: true }).click();
  await expect(page.getByText('Also PC-03.')).toBeVisible();
  await expect(page.getByLabel('Your reply')).toHaveValue('');
});

test('a customer sends a suggestion and sees our answers', async ({ page }) => {
  const api = await open(page);
  api.signedIn = true;
  await page.goto(`${portal}/suggestions`);
  await expect(page.getByText('Coming in 2.6.')).toBeVisible();
  await page.getByLabel('Title').fill('Dark mode');
  await page.getByLabel('Details').fill('For the night shift.');
  await page.getByRole('button', { name: 'Send suggestion' }).click();
  await expect(page.getByRole('status')).toContainText('Thank you');
  await expect(page.getByRole('heading', { name: 'Dark mode' })).toBeVisible();
});
