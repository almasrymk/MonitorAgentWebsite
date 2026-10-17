import { Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ACCOUNT_TEXTS } from './account/texts';
import { Api, ApiError } from './api';
import { DICTIONARY, I18n } from './i18n';
import { Me } from './models';
import { SUPPORT_TEXTS } from './support';

/** Test-only helpers for the shared library's specs. Not exported from the public API. */
export type FakeApi = { [K in 'get' | 'post' | 'put' | 'delete']: ReturnType<typeof vi.fn> };

export function fakeApi(): FakeApi {
  return { get: vi.fn().mockResolvedValue(undefined), post: vi.fn().mockResolvedValue(undefined), put: vi.fn(), delete: vi.fn() };
}

export const customer: Me = { id: 'u1', email: 'mona@example.test', fullName: 'Mona', emailConfirmed: true, mfaEnabled: false, roles: ['Customer'] };

export function apiError(status: number, code: string): ApiError {
  return new ApiError(status, code, code);
}

/** English texts, a fake API and a router; returns the fake so tests can script answers and check calls. */
export function setUp(extra: Provider[] = []): { api: FakeApi; router: Router; i18n: I18n } {
  const api = fakeApi();
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: Api, useValue: api },
      { provide: DICTIONARY, useValue: { en: { ...ACCOUNT_TEXTS.en, ...SUPPORT_TEXTS.en, 'error.generic': 'Something went wrong.' }, ar: { ...ACCOUNT_TEXTS.ar, ...SUPPORT_TEXTS.ar } } },
      ...extra,
    ],
  });
  const i18n = TestBed.inject(I18n);
  i18n.use('en');
  const router = TestBed.inject(Router);
  vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  return { api, router, i18n };
}

/** Lets pending promises (API calls the component awaits) settle, then renders. */
export async function settle(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }): Promise<void> {
  for (let i = 0; i < 5; i++) await Promise.resolve();
  await fixture.whenStable();
  fixture.detectChanges();
}
