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
  expect(results.violations.map((v) => `${v.id}: ${v.help} at ${v.nodes.map((n) => n.target.join(" ")).join(", ")}`)).toEqual([]);
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
  for (const path of ['', '/orders', '/releases', '/support', '/support/t1', '/suggestions', '/customers', '/customers/c1', '/reports', '/content', '/content/p1', '/content/p2', '/security', '/account']) {
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

test('support answers the oldest open ticket', async ({ page }) => {
  const api = signedIn(await open(page));
  await page.goto(`${admin}/support`);
  await expect(page.getByRole('cell', { name: 'Nile Traders' })).toBeVisible();
  await page.getByRole('link', { name: 'Agent stops at night' }).click();
  await page.getByLabel('Your reply').fill('Please send the service log.');
  await page.getByRole('button', { name: 'Send', exact: true }).click();
  await expect(page.getByText('Answered')).toBeVisible();
  expect(api.calls).toContain('POST /admin/support/tickets/t1/messages');
});

test('support answers a suggestion', async ({ page }) => {
  const api = signedIn(await open(page));
  await page.goto(`${admin}/suggestions`);
  const card = page.getByRole('article').filter({ hasText: 'Weekly report' });
  await card.getByLabel('Status', { exact: true }).selectOption('Planned');
  await card.getByLabel('Our answer').fill('Coming in 2.6.');
  await card.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('status')).toContainText('Weekly report');
  expect(api.suggestions[0]).toMatchObject({ status: 'Planned', staffNote: 'Coming in 2.6.' });
});

test('a reviewer publishes the version an editor sent', async ({ page }) => {
  const api = signedIn(await open(page));
  await page.goto(`${admin}/content`);
  await expect(page.getByRole('cell', { name: 'Waiting for review' })).toBeVisible();
  await page.getByRole('link', { name: 'About Monitor Agent' }).click();
  await expect(page.getByRole('heading', { name: 'Version 2 by Eman Editor is waiting for review' })).toBeVisible();
  await expect(page.getByRole('strong').filter({ hasText: 'Monitor Agent' }).first()).toBeVisible();
  await page.getByRole('button', { name: 'Approve and publish' }).click();
  await expect(page.getByRole('status')).toHaveText('Published. The website shows it within a minute.');
  await expect(page.getByText('Version 2 is live')).toBeVisible();
  expect(api.calls).toContain('POST /admin/content/pages/p1/approve');
});

test('an editor writes a draft and sends it for review, and cannot approve their own', async ({ page }) => {
  const api = signedIn(await open(page));
  await page.goto(`${admin}/content/p2`);
  await page.getByLabel('Text (English)').fill('## What we keep\n- Your name and email');
  await page.getByRole('button', { name: 'Save and send for review' }).click();
  await expect(page.getByRole('status')).toHaveText('Sent for review.');
  expect(api.pages[1].versions[0]).toMatchObject({ status: 'InReview', bodyEn: '## What we keep\n- Your name and email' });
  await page.getByRole('button', { name: 'Approve and publish' }).click();
  await expect(page.getByRole('alert')).toHaveText('Someone other than the last editor has to review this version.');
});

test('a reviewer sends a version back with a reason', async ({ page }) => {
  const api = signedIn(await open(page));
  await page.goto(`${admin}/content/p1`);
  await page.getByLabel('What needs to change').fill('Mention Cairo.');
  await page.getByRole('button', { name: 'Send back' }).click();
  await expect(page.getByText('Adel Admin sent this back: Mention Cairo.')).toBeVisible();
  expect(api.pages[0].versions[0]).toMatchObject({ status: 'Draft', reviewNote: 'Mention Cairo.' });
});

test('support finds a customer by email and sees its people and licenses', async ({ page }) => {
  signedIn(await open(page));
  await page.goto(`${admin}/customers`);
  await page.getByRole('searchbox', { name: 'Company name or exact email' }).fill('omar@delta.test');
  await page.getByRole('button', { name: 'Find' }).click();
  await expect(page).toHaveURL(/search=omar(@|%40)delta\.test/);
  await expect(page.getByRole('link', { name: 'Nile Traders' })).toHaveCount(0);
  await page.getByRole('link', { name: 'Delta Pharma' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Delta Pharma');
  await expect(page.getByRole('cell', { name: 'omar@delta.test' })).toBeVisible();
});

test('an admin issues a license by hand for a payment taken outside the website', async ({ page }) => {
  const api = signedIn(await open(page));
  await page.goto(`${admin}/customers/c1`);
  await expect(page.getByRole('cell', { name: 'LIC-2026-0003' })).toBeVisible();
  await page.getByLabel('Plan').selectOption('p-std');
  await page.getByLabel('Payment reference').fill('BANK-7781');
  await page.getByRole('button', { name: 'Issue license' }).click();
  await expect(page.getByRole('status')).toHaveText('License LIC-2026-0099 issued.');
  await expect(page.getByRole('cell', { name: 'LIC-2026-0099' })).toBeVisible();
  expect(api.grants).toEqual([{ companyId: 'c1', planId: 'p-std', orderReference: 'BANK-7781' }]);
});

test('finance picks a period for the money report and can download it', async ({ page }) => {
  const api = signedIn(await open(page));
  await page.goto(`${admin}/reports`);
  await page.getByLabel('From').fill('2026-10-01');
  await page.getByLabel('To').fill('2026-10-15');
  await page.getByRole('button', { name: 'Show' }).click();
  await expect(page).toHaveURL(/from=2026-10-01&to=2026-10-15/);
  await expect(page.getByRole('heading', { name: 'Net in EGP' })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'paymob' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Download as spreadsheet (CSV)' })).toHaveAttribute('href', '/api/v1/admin/reports/ledger.csv?from=2026-10-01&to=2026-10-15');
  expect(api.calls).toContain('GET /admin/reports/revenue?from=2026-10-01&to=2026-10-15');
});
