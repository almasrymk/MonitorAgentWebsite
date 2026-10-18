import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

const pages = ['', '/demo', '/pricing', '/download', '/terms', '/privacy', '/refunds', '/pages/about-us'];

// In production a reverse proxy serves /api from the same origin; here the browser's API calls go to the mock.
test.beforeEach(async ({ page }) => {
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    await route.fulfill({ response: await route.fetch({ url: `http://localhost:5099${url.pathname}${url.search}` }) });
  });
});

// Pages keep asking the API after a test's last check (the legal pages look for published text); let those go.
test.afterEach(async ({ page }) => {
  await page.unrouteAll({ behavior: 'ignoreErrors' });
});

for (const lang of ['ar', 'en'] as const) {
  for (const path of pages) {
    test(`/${lang}${path} has no accessibility violations`, async ({ page }) => {
      const blocked: string[] = [];
      page.on('console', (m) => m.type() === 'error' && m.text().includes('Content Security Policy') && blocked.push(m.text()));
      await page.goto(`/${lang}${path}`);
      await expect(page.locator('html')).toHaveAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');
      await expect(page.locator('h1')).toBeVisible();
      const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze();
      expect(results.violations.map((v) => `${v.id}: ${v.help} at ${v.nodes.map((n) => n.target.join(" ")).join(", ")}`)).toEqual([]);
      expect(blocked, 'blocked by the content security policy').toEqual([]);
    });
  }
}

test('pricing is rendered on the server with the catalog prices', async ({ request }) => {
  const html = await (await request.get('/en/pricing')).text();
  expect(html).toContain('Standard');
  expect(html).toContain('Professional');
  expect(html).toContain('/portal/checkout?plan=11111111-1111-1111-1111-111111111111');
});

test('the download page shows the newest version and its checksum', async ({ page }) => {
  await page.goto('/en/download');
  await expect(page.locator('#latest-title')).toHaveText('Version 2.4.0');
  await expect(page.locator('.checksum').first()).toHaveText('ab'.repeat(32));
  await expect(page.getByRole('link', { name: 'Download for win-x64' })).toHaveAttribute('href', /MonitorAgent-2\.4\.0/);
});

test('the language switch keeps the visitor on the same page', async ({ page }) => {
  await page.goto('/ar/pricing');
  await page.getByRole('link', { name: /التبديل إلى الإنجليزية/ }).click();
  await expect(page).toHaveURL(/\/en\/pricing$/);
  await expect(page.locator('h1')).toHaveText('Simple pricing per device');
});

test('the skip link moves keyboard users to the content', async ({ page }) => {
  await page.goto('/en');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'Skip to content' })).toBeFocused();
});

test('an unknown page answers 404 and an address without a language gets one', async ({ request, page }) => {
  expect((await request.get('/en/nothing-here')).status()).toBe(404);
  await page.goto('/pricing');
  await expect(page).toHaveURL(/\/ar\/pricing$/);
});

test('a published page is rendered on the server, and markup in it stays text', async ({ request, page }) => {
  const response = await request.get('/en/pages/about-us');
  expect(response.status()).toBe(200);
  const html = await response.text();
  expect(html).toContain('<title>About us | ');
  expect(html).toContain('<strong>Monitor Agent</strong>');
  expect(html).not.toContain('<script>window.hacked');

  await page.goto('/en/pages/about-us');
  await expect(page.getByRole('heading', { level: 2, name: 'Who we are' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'See prices' })).toHaveAttribute('href', '/en/pricing');
  await expect(page.getByText('<script>window.hacked = true</script>')).toBeVisible();
  expect(await page.evaluate(() => (window as unknown as { hacked?: boolean }).hacked)).toBeUndefined();
});

test('an unpublished page answers 404', async ({ request }) => {
  const response = await request.get('/ar/pages/not-yet');
  expect(response.status()).toBe(404);
  expect(await response.text()).toContain('الصفحة غير موجودة');
});

test('a published legal page replaces the built-in text, others keep it', async ({ page }) => {
  await page.goto('/en/privacy');
  await expect(page.getByText('Published privacy text from the content editor.')).toBeVisible();
  await page.goto('/en/terms');
  await expect(page.locator('h1')).toHaveText('Terms of service');
});

test('pages carry a per-request nonce and a strict content security policy', async ({ request }) => {
  const first = await request.get('/en');
  const second = await request.get('/en');
  const csp = first.headers()['content-security-policy'];
  expect(csp).toContain("frame-ancestors 'none'");
  expect(csp).toContain("object-src 'none'");
  const nonce = /'nonce-([^']+)'/.exec(csp)![1];
  expect(second.headers()['content-security-policy']).not.toContain(nonce);
  const html = await first.text();
  expect(html.match(/<script(?![^>]*nonce=)/g)).toBeNull();
  expect(html).toContain(`nonce="${nonce}"`);
  expect(first.headers()['x-powered-by']).toBeUndefined();
  expect(first.headers()['x-content-type-options']).toBe('nosniff');
});

test('a visitor asks for a demo', async ({ page }) => {
  let sent: Record<string, unknown> | undefined;
  await page.route('**/api/v1/auth/csrf', (route) =>
    route.fulfill({ status: 204, headers: { 'set-cookie': 'XSRF-TOKEN=token-1; Path=/; SameSite=Strict' } }));
  await page.route('**/api/v1/demo-requests', (route) => {
    expect(route.request().headers()['x-xsrf-token']).toBe('token-1');
    sent = route.request().postDataJSON();
    return route.fulfill({ status: 202 });
  });
  await page.goto('/en');
  await page.getByRole('link', { name: 'Contact us to request a demo' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Request a demo');
  await page.getByLabel('Your name').fill('Mona Adel');
  await page.getByLabel('Company').fill('Nile Gas Stations');
  await page.getByLabel('Email').fill('mona@nilegas.example');
  await page.getByLabel('Number of sites (optional)').fill('50');
  await page.getByRole('button', { name: 'Send request' }).click();
  await expect(page.getByRole('status')).toContainText('We received your request');
  expect(sent).toMatchObject({ name: 'Mona Adel', company: 'Nile Gas Stations', email: 'mona@nilegas.example', sites: 50, phone: null, language: 'en', website: '' });
});

test('the demo form says what went wrong', async ({ page }) => {
  await page.route('**/api/v1/auth/csrf', (route) => route.fulfill({ status: 204 }));
  await page.route('**/api/v1/demo-requests', (route) =>
    route.fulfill({ status: 429, contentType: 'application/json', body: JSON.stringify({ status: 429 }) }));
  await page.goto('/ar/demo');
  await page.getByLabel('الاسم').fill('منى');
  await page.getByLabel('الشركة').fill('محطات النيل');
  await page.getByLabel('البريد الإلكتروني').fill('mona@nilegas.example');
  await page.getByRole('button', { name: 'أرسل الطلب' }).click();
  await expect(page.getByRole('alert')).toContainText('انتظر دقيقة');
});
