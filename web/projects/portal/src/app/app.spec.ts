import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiError, DICTIONARY, errorText, I18n, Loader, Session } from 'shared';
import { PORTAL_TEXTS } from './texts';

describe('portal', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DICTIONARY, useValue: PORTAL_TEXTS }],
    });
  });

  it('has every text in both languages', () => {
    expect(Object.keys(PORTAL_TEXTS.ar).sort()).toEqual(Object.keys(PORTAL_TEXTS.en).sort());
  });

  it('explains API errors in the user language and falls back to a general message', () => {
    const i18n = TestBed.inject(I18n);
    i18n.use('en');
    const known = new ApiError(400, 'INVALID_CREDENTIALS', 'x');
    expect(errorText(i18n, known)).toBe(PORTAL_TEXTS.en['error.INVALID_CREDENTIALS']);
    expect(errorText(i18n, new ApiError(500, 'SOMETHING_NEW', 'x'))).toBe(PORTAL_TEXTS.en['error.generic']);
    expect(errorText(i18n, new Error('network'))).toBe(PORTAL_TEXTS.en['error.generic']);
  });

  it('treats 401 from /account/me as signed out and remembers the answer', async () => {
    const session = TestBed.inject(Session);
    const http = TestBed.inject(HttpTestingController);
    const first = session.load();
    http.expectOne('/api/v1/account/me').flush({ code: 'UNAUTHENTICATED' }, { status: 401, statusText: 'Unauthorized' });
    expect(await first).toBeNull();
    expect(await session.load()).toBeNull();
    http.verify();
  });

  it('does not hide server failures as signed out', async () => {
    const session = TestBed.inject(Session);
    const pending = session.load();
    TestBed.inject(HttpTestingController).expectOne('/api/v1/account/me').flush({ code: 'X' }, { status: 503, statusText: 'Unavailable' });
    await expect(pending).rejects.toBeInstanceOf(ApiError);
  });

  it('keeps the last value while reloading quietly', async () => {
    let n = 0;
    const loader = new Loader(async () => ++n);
    await loader.load();
    const reload = loader.load(true);
    expect(loader.loading()).toBe(false);
    expect(loader.value()).toBe(1);
    await reload;
    expect(loader.value()).toBe(2);
  });
});
