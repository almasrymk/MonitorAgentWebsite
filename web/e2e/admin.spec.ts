import AxeBuilder from '@axe-core/playwright';
import { expect, Page, test } from '@playwright/test';
import { FakeAdminApi } from './fake-admin-api';

const admin = 'http://localhost:4402/admin';

async function open(page: Page, lang: 'ar' | 'en' = 'en'): Promise<FakeAdminApi> {
  const api = new FakeAdminApi();
  await api.install(page);
  await page.addInitScript((l) => localStorage.setItem('ma.lang', l), lang);
  return api;
}

function signedIn(api: FakeAdminApi): FakeAdminApi {
  api.signedIn = true;
  api.sessionHasMfa = true;
  return api;
}

async function expectAccessible(page: Page): Promise<void> {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze();
  expect(results.violations.map((v) => `${v.id}: ${v.help} (${v.nodes.length})`)).toEqual([]);
}

test('staff sign in with their authenticator code', async ({ page }) => {
  const api = await open(page);
  await page.goto(`${admin}/releases`);
  await expect(page).toHaveURL(/\/admin\/sign-in\?returnUrl=%2Freleases/);
  await expect(page.getByRole('link', { name: 'Create an account' })).toHaveCount(0);
  await expectAccessible(page);
  await page.getByLabel('Email').fill('admin@example.test');
  await page.getByLabel('Password', { exact: true }).fill('correct horse battery');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.getByLabel('Code', { exact: true }).fill('123456');
  await page.getByRole('button', { name: 'Verify' }).click();
  await expect(page).toHaveURL(`${admin}/releases`);
  expect(api.calls).toContain('POST /auth/login/mfa');
});

test('a staff member without two-step verification has to turn it on and sign in again', async ({ page }) => {
  const api = await open(page);
  api.signedIn = true;
  api.me.mfaEnabled = false;
  await page.goto(`${admin}/orders`);
  await expect(page).toHaveURL(`${admin}/account?setup=mfa`);
  await expect(page.getByRole('alert')).toContainText('two-step verification');
  await page.getByRole('button', { name: /set up|turn on/i }).first().click();
  await page.getByLabel('Code', { exact: true }).fill('123456');
  await page.getByRole('button', { name: /turn on|enable/i }).click();
  await page.getByRole('button', { name: 'Sign in again' }).click();
  await expect(page).toHaveURL(`${admin}/sign-in`);
});

test('a customer account is told this area is for staff', async ({ page }) => {
  const api = await open(page);
  api.signedIn = true;
  api.me.roles = ['Customer'];
  await page.goto(`${admin}/`);
  await expect(page).toHaveURL(`${admin}/no-access`);
  await expect(page.getByRole('link', { name: 'Open the portal' })).toHaveAttribute('href', '/portal/');
  await expectAccessible(page);
});

test('finance records a bank transfer for a pending order', async ({ page }) => {
  const api = signedIn(await open(page));
  await page.goto(`${admin}/`);
  await page.getByRole('link', { name: /awaiting payment/i }).click();
  await expect(page).toHaveURL(`${admin}/orders?status=PendingPayment`);
  await expect(page.getByRole('cell', { name: 'ORD-2026-000002' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Record payment' }).click();
  await page.getByPlaceholder('Bank or receipt reference').fill('CIB-778812');
  await page.getByRole('button', { name: 'Confirm' }).click();
  await expect(page.getByRole('status')).toContainText('ORD-2026-000001');
  expect(api.calls).toContain('POST /admin/orders/o1/manual-payment');
});

test('a signed release is uploaded byte for byte, then published', async ({ page }) => {
  const api = signedIn(await open(page));
  page.on('dialog', (dialog) => void dialog.accept());
  await page.goto(`${admin}/releases`);
  const manifest = Buffer.from('{"version":"2.4.0","notes":"Faster ✓"}\n', 'utf8');
  await page.getByLabel('Manifest (manifest.json)').setInputFiles({ name: 'manifest.json', mimeType: 'application/json', buffer: manifest });
  await page.getByLabel('Signature (manifest.json.sig)').setInputFiles({ name: 'manifest.json.sig', mimeType: 'text/plain', buffer: Buffer.from('MEUCIQ==\n') });
  await page.getByRole('button', { name: 'Upload draft' }).click();
  await expect(page.getByRole('status')).toContainText('2.4.0');
  expect(Buffer.from(api.uploaded!.manifestBase64, 'base64').equals(manifest)).toBe(true);
  expect(api.uploaded!.signature).toBe('MEUCIQ==');
  expect(api.uploaded!.channel).toBe('stable');

  await page.getByRole('button', { name: 'Publish' }).click();
  await expect(page.getByRole('status')).toContainText('2.4.0 is published');
});

test('an expired MFA session explains what to do', async ({ page }) => {
  const api = await open(page);
  api.signedIn = true;
  await page.goto(`${admin}/releases`);
  await expect(page.getByRole('alert')).toContainText('sign in again');
});

for (const lang of ['ar', 'en'] as const) {
  for (const path of ['', '/orders', '/releases', '/security', '/account']) {
    test(`${lang} ${path || '/'} has no accessibility violations`, async ({ page }) => {
      signedIn(await open(page, lang));
      await page.goto(`${admin}${path}`);
      await expect(page.locator('html')).toHaveAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      await expect(page.locator('.skeleton')).toHaveCount(0);
      await expectAccessible(page);
      expect(await page.evaluate(() => window.innerWidth), 'page is wider than the screen').toBe(page.viewportSize()!.width);
    });
  }
}
