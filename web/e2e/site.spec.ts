import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

const pages = ['', '/pricing', '/download', '/terms', '/privacy', '/refunds'];

// In production a reverse proxy serves /api from the same origin; here the browser's API calls go to the mock.
test.beforeEach(async ({ page }) => {
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url());
    await route.fulfill({ response: await route.fetch({ url: `http://localhost:5099${url.pathname}${url.search}` }) });
  });
});

for (const lang of ['ar', 'en'] as const) {
  for (const path of pages) {
    test(`/${lang}${path} has no accessibility violations`, async ({ page }) => {
      await page.goto(`/${lang}${path}`);
      await expect(page.locator('html')).toHaveAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');
      await expect(page.locator('h1')).toBeVisible();
      const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze();
      expect(results.violations.map((v) => `${v.id}: ${v.help} at ${v.nodes.map((n) => n.target.join(" ")).join(", ")}`)).toEqual([]);
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
