import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { DICTIONARY, I18n } from 'shared';
import { DownloadPage } from './pages/download';
import { PricingPage } from './pages/pricing';
import { SITE_TEXTS } from './texts';

/** Lets the API promise resolve and the signals render. */
async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve));
  await fixture.whenStable();
  fixture.detectChanges();
}

describe('site', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: DICTIONARY, useValue: SITE_TEXTS }],
    });
  });

  it('has every text in both languages', () => {
    expect(Object.keys(SITE_TEXTS.ar).sort()).toEqual(Object.keys(SITE_TEXTS.en).sort());
  });

  it('switches the page to right-to-left for Arabic', () => {
    const i18n = TestBed.inject(I18n);
    i18n.use('en');
    expect(document.documentElement.dir).toBe('ltr');
    i18n.use('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(document.documentElement.lang).toBe('ar');
  });

  it('shows the catalog prices and links each plan to checkout', async () => {
    TestBed.inject(I18n).use('en');
    const fixture = TestBed.createComponent(PricingPage);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/catalog/plans')
      .flush([
        { id: 'p2', productCode: 'UBG', productName: 'UBG Monitor', code: 'PRO', name: 'Professional', price: 2400, currency: 'EGP', durationDays: 365, maxDevices: 10, features: ['Alerts'] },
        { id: 'p1', productCode: 'UBG', productName: 'UBG Monitor', code: 'STD', name: 'Standard', price: 1200, currency: 'EGP', durationDays: 365, maxDevices: 3, features: [] },
      ]);
    await settle(fixture);

    const cards = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.plan')];
    expect(cards.map((c) => c.querySelector('h2')?.textContent?.trim())).toEqual(['Standard', 'Professional']);
    expect(cards[0].textContent).toContain('Up to 3 devices');
    expect(cards[0].querySelector('a')?.getAttribute('href')).toBe('/portal/checkout?plan=p1');
  });

  it('says so when prices cannot be loaded', async () => {
    TestBed.inject(I18n).use('en');
    const fixture = TestBed.createComponent(PricingPage);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/v1/catalog/plans').flush(null, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect((fixture.nativeElement as HTMLElement).querySelector('[role=alert]')?.textContent).toContain('could not be loaded');
  });

  it('offers the newest stable version with its checksum', async () => {
    TestBed.inject(I18n).use('en');
    const fixture = TestBed.createComponent(DownloadPage);
    fixture.detectChanges();
    const pkg = { platform: 'win-x64', url: 'https://downloads.example.test/a.zip', sha256: 'a'.repeat(64), size: 1048576 };
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/releases')
      .flush([
        { id: '1', version: '1.0.0', channel: 'stable', notes: null, status: 'Published', createdAt: '', publishedAt: '2026-09-01T00:00:00Z', packages: [pkg] },
        { id: '2', version: '1.1.0', channel: 'stable', notes: 'Faster', status: 'Published', createdAt: '', publishedAt: '2026-10-01T00:00:00Z', packages: [pkg] },
      ]);
    await settle(fixture);

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('#latest-title')?.textContent).toContain('1.1.0');
    expect(page.querySelector('.checksum')?.textContent).toBe('a'.repeat(64));
    expect(page.textContent).toContain('1 MB');
  });
});
